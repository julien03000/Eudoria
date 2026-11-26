// EUDORIA — generates the shared entry ticket test vectors (plan 1.6, section 8.2; ADR-0007).
// File-based C# program, run by new-entry-ticket-vectors.ps1 with "dotnet run --file": nothing to install.
// Writes <output>/keys.json (JWK Set: PUBLIC keys only) and <output>/vectors.json (signed tickets, the
// time to check them at, and the expected verdict). The private keys exist only in memory while this
// runs: no private key ever lands in the repository. Each run makes new keys, so new files.
// Every verifier of entry tickets (.NET third party, C++ of the Unreal plugin) must give exactly the
// expected verdict for every vector.
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: NewEntryTicketVectors.cs <output directory>");
    return 1;
}

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);

const string World = "sandbox";
const string Accepted = "accepted";
long iat = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
string accountId = "0193f5a2-7c1e-7b3a-9d4e-2f6a8b0c1d2e";

using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using ECDsa impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256); // same kid, other key: never published
using ECDsa unpublished = ECDsa.Create(ECCurve.NamedCurves.nistP256);
(string x, string y) = Coordinates(key);
string kid = Thumbprint(key);
string unpublishedKid = Thumbprint(unpublished);

List<JsonObject> vectors = [];

// ---------- Accepted, and the edges of time ----------
Add("valid", Ticket(), iat, Accepted, "A ticket exactly as the core issues it.");
Add("valid-at-expiry-plus-tolerance", Ticket(), iat + 75, Accepted, "exp + 15 s: the last accepted second.");
Add("expired", Ticket(), iat + 76, "ticket_expired", "exp + 16 s.");
Add("valid-issued-tolerance-in-future", Ticket(), iat - 15, Accepted, "iat 15 s ahead of the world's clock: accepted.");
Add("issued-in-future", Ticket(), iat - 16, "ticket_from_future", "iat 16 s ahead of the world's clock.");

// ---------- Audience, issuer, type, algorithm ----------
Add("other-world", Ticket(claims: c => c["aud"] = "eudoria-world:other"), iat, "aud_invalid", "Ticket for another world.");
Add("aud-as-list", Ticket(claims: c => c["aud"] = new JsonArray("eudoria-world:sandbox")), iat, "aud_invalid", "aud must be one string, never a list.");
Add("aud-missing", Ticket(claims: c => c.Remove("aud")), iat, "aud_invalid", "No aud.");
Add("other-issuer", Ticket(claims: c => c["iss"] = "someone-else"), iat, "iss_invalid", "iss is not eudoria-core.");
Add("access-token", Ticket(header: h => h["typ"] = "eudoria-access+jwt", claims: c => c["aud"] = "eudoria-core"), iat, "typ_not_allowed",
    "Shaped like a core access token (type and audience).");
Add("typ-missing", Ticket(header: h => h.Remove("typ")), iat, "typ_not_allowed", "No typ.");
Add("typ-generic-jwt", Ticket(header: h => h["typ"] = "JWT"), iat, "typ_not_allowed", "Generic type JWT.");
Add("typ-other-case", Ticket(header: h => h["typ"] = "Eudoria-Entry+JWT"), iat, "typ_not_allowed", "typ is compared case-sensitively.");
Add("alg-none", $"{Encode($$"""{"alg":"none","typ":"eudoria-entry+jwt","kid":"{{kid}}"}""")}.{Part(Ticket(), 1)}.", iat, "alg_not_allowed",
    "Unsigned (alg none).");
Add("alg-hs256-public-key-as-secret", HmacConfusion(Ticket()), iat, "alg_not_allowed", "HS256 with the public JWK Set as the secret.");
Add("alg-es384", Ticket(header: h => h["alg"] = "ES384"), iat, "alg_not_allowed", "Only ES256.");

// ---------- Key and signature ----------
Add("kid-unknown", Ticket(signer: unpublished, keyId: unpublishedKid), iat, "kid_unknown", "Signed by a key that is not published.");
Add("tampered-payload", Replace(Ticket(), 1, Encode(Claims(c => c["sub"] = "0193f5a2-0000-7000-8000-000000000000").ToJsonString())), iat,
    "signature_invalid", "Another account, the original signature.");
Add("tampered-signature", FlipSignatureBit(Ticket()), iat, "signature_invalid", "One bit of the signature changed.");
Add("signature-der", DerSigned(Ticket()), iat, "signature_invalid", "Valid signature in DER format instead of r||s (64 bytes).");
Add("signature-r-s-swapped", SwapRAndS(Ticket()), iat, "signature_invalid", "r and s exchanged.");
Add("signature-empty", Replace(Ticket(), 2, string.Empty), iat, "signature_invalid", "Empty signature part.");
Add("signed-by-another-key-same-kid", Ticket(signer: impostor), iat, "signature_invalid", "The published kid, another key.");

// ---------- Structure ----------
Add("header-extra-member", Ticket(header: h => h["cty"] = "JWT"), iat, "ticket_malformed", "The header holds only alg, typ and kid.");
Add("header-jku", Ticket(header: h => h["jku"] = "https://attacker.example/keys"), iat, "ticket_malformed", "Key location headers are refused.");
Add("header-crit", Ticket(header: h => h["crit"] = new JsonArray("exp")), iat, "ticket_malformed", "crit is refused.");
Add("kid-missing", Ticket(header: h => h.Remove("kid")), iat, "ticket_malformed", "No kid.");
Add("kid-not-a-string", Ticket(header: h => h["kid"] = 7), iat, "ticket_malformed", "Header members are strings.");
Add("duplicate-claim", Sign(Header().ToJsonString(),
    Claims().ToJsonString().Replace("{\"iss\"", "{\"sub\":\"0193f5a2-0000-7000-8000-000000000000\",\"iss\"", StringComparison.Ordinal)),
    iat, "ticket_malformed", "sub written twice (JSON parsers disagree on which one wins).");
Add("duplicate-header-member", Sign(Header().ToJsonString().Replace("{\"alg\"", "{\"alg\":\"none\",\"alg\"", StringComparison.Ordinal),
    Claims().ToJsonString()), iat, "ticket_malformed", "alg written twice.");
Add("two-parts", $"{Part(Ticket(), 0)}.{Part(Ticket(), 1)}", iat, "ticket_malformed", "No signature part.");
Add("four-parts", Ticket() + ".AAAA", iat, "ticket_malformed", "One part too many.");
Add("base64-padding", Replace(Ticket(), 2, Convert.ToBase64String(Base64Url.DecodeFromChars(Part(Ticket(), 2))).Replace('+', '-').Replace('/', '_')), iat,
    "ticket_malformed", "Padding '=' is not allowed.");
Add("base64-standard-alphabet", StandardAlphabetSignature(), iat, "ticket_malformed", "'+' and '/' are not Base64 URL-safe.");
Add("base64-non-canonical", Replace(Ticket(), 2, NonCanonical(Part(Ticket(), 2))), iat, "ticket_malformed",
    "Last character with non-zero unused bits: decodes to the same bytes, but only one encoding is accepted.");
Add("payload-invalid-utf8", SignBytes(Encoding.UTF8.GetBytes(Header().ToJsonString()), InvalidUtf8In(Claims().ToJsonString())), iat,
    "ticket_malformed", "Signed, but the payload is not valid UTF-8 (0xC3 0x28 in a member name).");
Add("header-invalid-utf8", SignBytes(InvalidUtf8In(Header().ToJsonString()), Encoding.UTF8.GetBytes(Claims().ToJsonString())), iat,
    "ticket_malformed", "Signed, but the header is not valid UTF-8.");
Add("payload-not-json",Sign(Header().ToJsonString(), "not json"), iat, "ticket_malformed", "Signed, but not JSON.");
Add("payload-array", Sign(Header().ToJsonString(), "[1,2]"), iat, "ticket_malformed", "Signed, but not a JSON object.");
Add("too-long", Ticket(claims: c => c["pad"] = new string('a', 4000)), iat, "ticket_malformed", "More than 4096 characters.");

// ---------- Claims ----------
Add("extra-claim-ignored", Ticket(claims: c => c["nbf"] = iat), iat, Accepted, "Unknown claims are ignored (only the header is closed).");
Add("sub-not-uuid", Ticket(claims: c => c["sub"] = "ana"), iat, "claims_invalid", "sub is not a UUID.");
Add("sub-upper-case", Ticket(claims: c => c["sub"] = accountId.ToUpperInvariant()), iat, "claims_invalid", "sub in lower case only.");
Add("sub-missing", Ticket(claims: c => c.Remove("sub")), iat, "claims_invalid", "No sub.");
Add("jti-missing", Ticket(claims: c => c.Remove("jti")), iat, "claims_invalid", "No jti.");
Add("jti-empty", Ticket(claims: c => c["jti"] = string.Empty), iat, "claims_invalid", "Empty jti.");
Add("exp-as-text", Ticket(claims: c => c["exp"] = (iat + 60).ToString(System.Globalization.CultureInfo.InvariantCulture)), iat, "claims_invalid",
    "exp is a number, not text.");
Add("exp-fractional", Ticket(claims: c => c["exp"] = iat + 60.5), iat, "claims_invalid", "Whole seconds only.");
Add("exp-before-iat", Ticket(claims: c => c["exp"] = iat), iat, "claims_invalid", "exp must be after iat.");
Add("iat-missing", Ticket(claims: c => c.Remove("iat")), iat, "claims_invalid", "No iat.");

JsonObject keys = new()
{
    ["keys"] = new JsonArray(new JsonObject
    {
        ["kty"] = "EC", ["crv"] = "P-256", ["x"] = x, ["y"] = y, ["kid"] = kid, ["alg"] = "ES256", ["use"] = "sig",
    }),
};
JsonObject document = new()
{
    ["description"] = "EUDORIA entry ticket test vectors (ADR-0007). Check each ticket for the world below, at the time 'now' "
        + "(Unix seconds), with the public keys of keys.json; the verdict must be 'expected' ('accepted' or a rejection code). "
        + "Generated by tools/dev/new-entry-ticket-vectors.ps1.",
    ["world"] = World,
    ["accountId"] = accountId,
    ["clockToleranceSeconds"] = 15,
    ["vectors"] = new JsonArray([.. vectors]),
};

JsonSerializerOptions indented = new() { WriteIndented = true };
File.WriteAllText(Path.Combine(output, "keys.json"), keys.ToJsonString(indented) + "\n", new UTF8Encoding(false));
File.WriteAllText(Path.Combine(output, "vectors.json"), document.ToJsonString(indented) + "\n", new UTF8Encoding(false));
Console.WriteLine($"{vectors.Count} vectors written to {output}");
return 0;

// ---------- Helpers ----------

void Add(string name, string ticket, long now, string expected, string why)
{
    if (vectors.Any(vector => vector["name"]!.GetValue<string>() == name))
    {
        throw new InvalidOperationException($"duplicate vector name {name}");
    }

    vectors.Add(new JsonObject { ["name"] = name, ["why"] = why, ["now"] = now, ["expected"] = expected, ["ticket"] = ticket });
}

JsonObject Header() => new() { ["alg"] = "ES256", ["typ"] = "eudoria-entry+jwt", ["kid"] = kid };

JsonObject Claims(Action<JsonObject>? change = null)
{
    JsonObject claims = new()
    {
        ["iss"] = "eudoria-core",
        ["sub"] = accountId,
        ["aud"] = "eudoria-world:" + World,
        ["iat"] = iat,
        ["exp"] = iat + 60,
        ["jti"] = Guid.NewGuid().ToString(),
    };
    change?.Invoke(claims);
    return claims;
}

string Ticket(Action<JsonObject>? header = null, Action<JsonObject>? claims = null, ECDsa? signer = null, string? keyId = null)
{
    JsonObject h = Header();
    if (keyId is not null)
    {
        h["kid"] = keyId;
    }

    header?.Invoke(h);
    return Sign(h.ToJsonString(), Claims(claims).ToJsonString(), signer);
}

string Sign(string headerJson, string payloadJson, ECDsa? signer = null)
{
    string signingInput = Encode(headerJson) + "." + Encode(payloadJson);
    byte[] signature = (signer ?? key).SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    return signingInput + "." + Base64Url.EncodeToString(signature);
}

string SignBytes(byte[] header, byte[] payload)
{
    string signingInput = Base64Url.EncodeToString(header) + "." + Base64Url.EncodeToString(payload);
    byte[] signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    return signingInput + "." + Base64Url.EncodeToString(signature);
}

// The same bytes, last character with its unused low bits set.
static string NonCanonical(string part)
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    return part[..^1] + Alphabet[Alphabet.IndexOf(part[^1], StringComparison.Ordinal) | 1];
}

// UTF-8 of the JSON text with 0xC3 0x28 (not UTF-8) at the start of the first member name.
static byte[] InvalidUtf8In(string json)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json.Insert(2, "ÿ"));
    bytes[3] = 0x28;
    return bytes;
}

static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

static string Part(string ticket, int index) => ticket.Split('.')[index];

static string Replace(string ticket, int index, string part)
{
    string[] parts = ticket.Split('.');
    parts[index] = part;
    return string.Join('.', parts);
}

static string FlipSignatureBit(string ticket)
{
    byte[] signature = Base64Url.DecodeFromChars(Part(ticket, 2));
    signature[10] ^= 0x01;
    return Replace(ticket, 2, Base64Url.EncodeToString(signature));
}

static string SwapRAndS(string ticket)
{
    byte[] signature = Base64Url.DecodeFromChars(Part(ticket, 2));
    return Replace(ticket, 2, Base64Url.EncodeToString([.. signature[32..], .. signature[..32]]));
}

string DerSigned(string ticket)
{
    string signingInput = ticket[..ticket.LastIndexOf('.')];
    byte[] der = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    return Replace(ticket, 2, Base64Url.EncodeToString(der));
}

string HmacConfusion(string ticket)
{
    string header = Encode($$"""{"alg":"HS256","typ":"eudoria-entry+jwt","kid":"{{kid}}"}""");
    string signingInput = header + "." + Part(ticket, 1);
    byte[] secret = Encoding.UTF8.GetBytes(PublishedKeysJson());
    return signingInput + "." + Base64Url.EncodeToString(HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(signingInput)));
}

string PublishedKeysJson() => $$"""{"keys":[{"kty":"EC","crv":"P-256","x":"{{x}}","y":"{{y}}","kid":"{{kid}}","alg":"ES256","use":"sig"}]}""";

// A signature whose standard Base64 has '+' or '/': signs again until it does.
string StandardAlphabetSignature()
{
    while (true)
    {
        string ticket = Ticket();
        string standard = Convert.ToBase64String(Base64Url.DecodeFromChars(Part(ticket, 2))).TrimEnd('=');
        if (standard.Contains('+') || standard.Contains('/'))
        {
            return Replace(ticket, 2, standard);
        }
    }
}

static (string X, string Y) Coordinates(ECDsa ecdsa)
{
    ECParameters parameters = ecdsa.ExportParameters(includePrivateParameters: false);
    return (Base64Url.EncodeToString(parameters.Q.X), Base64Url.EncodeToString(parameters.Q.Y));
}

// RFC 7638 thumbprint, as the core computes it.
static string Thumbprint(ECDsa ecdsa)
{
    (string x, string y) = Coordinates(ecdsa);
    return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes($$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""")));
}
