using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace Eudoria.ThirdParty.WorldVerifier;

/// <summary>A ticket whose every rule passed: who the player is, and what the replay memory needs.</summary>
public sealed record CheckedTicket(Guid AccountId, string Jti, long ExpiresAtUnixSeconds);

/// <summary>Either a checked ticket, or the rejection code (<see cref="TicketRejections"/>).</summary>
public sealed record TicketCheck(CheckedTicket? Ticket, string? Rejection, string? KeyId = null)
{
    public static TicketCheck Refused(string rejection) => new(null, rejection);
}

/// <summary>
/// The verification rules of an EUDORIA entry ticket (contract core-v1, tag "worlds"; ADR-0007), as a
/// pure function: the ticket, the expected world, the current time and the known public keys in, a
/// verdict out. No network, no memory: the replay check is done by the caller (<see cref="WorldTicketVerifier"/>).
/// Checks, in this exact order (the shared test vectors depend on it):
/// 1. structure (three Base64 URL-safe parts, JSON objects, no duplicate member, header = alg/typ/kid only);
/// 2. alg = "ES256"; 3. typ = "eudoria-entry+jwt"; 4. key found by kid; 5. ES256 signature (r||s, 64 bytes);
/// 6. iss; 7. aud (one string); 8. sub, iat, exp, jti well-formed, exp after iat; 9. not expired; 10. not from the future.
/// </summary>
public static class EntryTicketRules
{
    public const string ExpectedAlgorithm = "ES256";
    public const string ExpectedType = "eudoria-entry+jwt";
    public const string ExpectedIssuer = "eudoria-core";
    public const string AudiencePrefix = "eudoria-world:";

    /// <summary>Allowed clock difference between the core and the world (owner decision: 15 s).</summary>
    public const long ClockToleranceSeconds = 15;

    /// <summary>Far above a real ticket (about 450 characters): refuses huge inputs before decoding anything.</summary>
    public const int MaxTicketLength = 4096;

    public const int MaxJtiLength = 128;

    private static readonly JsonDocumentOptions StrictJson = new() { AllowDuplicateProperties = false, MaxDepth = 4 };

    public static string AudienceOf(string worldId) => AudiencePrefix + worldId;

    /// <summary>Steps 1 to 3: the structure and the header. On success, <see cref="TicketCheck.KeyId"/> is the ticket's kid.</summary>
    public static TicketCheck ReadHeader(string ticket)
    {
        if (!TrySplit(ticket, out byte[] header, out _, out _))
        {
            return TicketCheck.Refused(TicketRejections.Malformed);
        }

        using JsonDocument? document = TryParseObject(header);
        if (document is null)
        {
            return TicketCheck.Refused(TicketRejections.Malformed);
        }

        string? algorithm = null;
        string? type = null;
        string? keyId = null;
        foreach (JsonProperty member in document.RootElement.EnumerateObject())
        {
            // Only these three, as strings: "crit", "jku", "jwk", "x5u"... are never honoured, so refused.
            if (member.Value.ValueKind != JsonValueKind.String)
            {
                return TicketCheck.Refused(TicketRejections.Malformed);
            }

            switch (member.Name)
            {
                case "alg": algorithm = member.Value.GetString(); break;
                case "typ": type = member.Value.GetString(); break;
                case "kid": keyId = member.Value.GetString(); break;
                default: return TicketCheck.Refused(TicketRejections.Malformed);
            }
        }

        // Exact, case-sensitive comparisons: we write these values ourselves.
        if (algorithm != ExpectedAlgorithm)
        {
            return TicketCheck.Refused(TicketRejections.AlgorithmNotAllowed);
        }

        if (type != ExpectedType)
        {
            return TicketCheck.Refused(TicketRejections.TypeNotAllowed);
        }

        return string.IsNullOrEmpty(keyId)
            ? TicketCheck.Refused(TicketRejections.Malformed)
            : new TicketCheck(null, null, keyId);
    }

    /// <summary>Every rule (steps 1 to 10) for the given world, at the given time, with the given public keys (by kid).</summary>
    public static TicketCheck Check(string ticket, string worldId, DateTimeOffset now, IReadOnlyDictionary<string, ECParameters> keys)
    {
        TicketCheck header = ReadHeader(ticket);
        if (header.Rejection is not null)
        {
            return header;
        }

        if (!keys.TryGetValue(header.KeyId!, out ECParameters publicKey))
        {
            return TicketCheck.Refused(TicketRejections.KeyUnknown);
        }

        TrySplit(ticket, out _, out byte[] payload, out byte[] signature);
        int signedLength = ticket.LastIndexOf('.');
        if (!VerifyEs256(publicKey, Encoding.ASCII.GetBytes(ticket[..signedLength]), signature))
        {
            return TicketCheck.Refused(TicketRejections.SignatureInvalid);
        }

        using JsonDocument? document = TryParseObject(payload);
        if (document is null)
        {
            return TicketCheck.Refused(TicketRejections.Malformed);
        }

        JsonElement claims = document.RootElement;
        if (StringClaim(claims, "iss") != ExpectedIssuer)
        {
            return TicketCheck.Refused(TicketRejections.IssuerInvalid);
        }

        // A list is refused, even one holding only our audience: the ticket names one world.
        if (StringClaim(claims, "aud") != AudienceOf(worldId))
        {
            return TicketCheck.Refused(TicketRejections.AudienceInvalid);
        }

        string? subject = StringClaim(claims, "sub");
        string? jti = StringClaim(claims, "jti");
        long? issuedAt = IntegerClaim(claims, "iat");
        long? expiresAt = IntegerClaim(claims, "exp");
        if (!IsLowerCaseUuid(subject) || string.IsNullOrEmpty(jti) || jti.Length > MaxJtiLength
            || issuedAt is null || expiresAt is null || expiresAt <= issuedAt)
        {
            return TicketCheck.Refused(TicketRejections.ClaimsInvalid);
        }

        long nowSeconds = now.ToUnixTimeSeconds();
        if (nowSeconds > expiresAt + ClockToleranceSeconds)
        {
            return TicketCheck.Refused(TicketRejections.Expired);
        }

        if (issuedAt > nowSeconds + ClockToleranceSeconds)
        {
            return TicketCheck.Refused(TicketRejections.IssuedInTheFuture);
        }

        return new TicketCheck(new CheckedTicket(Guid.ParseExact(subject!, "D"), jti, expiresAt.Value), null, header.KeyId);
    }

    /// <summary>
    /// ES256 (RFC 7518, section 3.4): ECDSA P-256 + SHA-256; the JWS signature is r||s, 32 bytes each
    /// (not the DER format some libraries produce).
    /// </summary>
    private static bool VerifyEs256(ECParameters publicKey, byte[] signingInput, byte[] signature)
    {
        if (signature.Length != 64)
        {
            return false;
        }

        using ECDsa key = ECDsa.Create(publicKey);
        return key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Three parts separated by '.', each strict Base64 URL-safe without padding; header and payload not empty.</summary>
    private static bool TrySplit(string ticket, out byte[] header, out byte[] payload, out byte[] signature)
    {
        header = payload = signature = [];
        if (ticket.Length > MaxTicketLength)
        {
            return false;
        }

        string[] parts = ticket.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        return TryDecode(parts[0], out header) && TryDecode(parts[1], out payload) && TryDecode(parts[2], out signature);
    }

    /// <summary>
    /// Strict Base64 URL-safe decoding: only A-Z a-z 0-9 - _, no padding, no whitespace, and the unused
    /// bits of the last character must be zero (one encoding per value: "QR" is refused, "QQ" is not).
    /// </summary>
    public static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length % 4 == 1 || !text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }

        try
        {
            bytes = Base64Url.DecodeFromChars(text);
            return true;
        }
        catch (FormatException)
        {
            return false; // non-zero unused bits in the last character
        }
    }

    /// <summary>A JSON object, read strictly: valid UTF-8, no duplicate member. Null otherwise.</summary>
    internal static JsonDocument? TryParseObject(byte[] json)
    {
        // Checked first: with invalid UTF-8, reading a name or a string throws InvalidOperationException.
        if (!Utf8.IsValid(json))
        {
            return null;
        }

        try
        {
            JsonDocument document = JsonDocument.Parse(json, StrictJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
        }
        catch (JsonException)
        {
            // not JSON, or a duplicate member
        }

        return null;
    }

    private static string? StringClaim(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A NumericDate written as a whole number (what the core issues); 1.5 or "1" are refused.</summary>
    private static long? IntegerClaim(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number
            : null;

    /// <summary>8-4-4-4-12 lower-case hexadecimal digits, as the core writes account identifiers.</summary>
    private static bool IsLowerCaseUuid(string? value) =>
        value is { Length: 36 }
        && value.Select((c, index) => index is 8 or 13 or 18 or 23 ? c == '-' : char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)).All(ok => ok);
}
