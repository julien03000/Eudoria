namespace Eudoria.ThirdParty.WorldVerifier;

/// <summary>
/// Why a ticket was refused. The same codes, checked in the same order, are expected from the C++
/// verifier (shared test vectors: shared/test-vectors/entry-tickets/).
/// </summary>
public static class TicketRejections
{
    /// <summary>Not three Base64 URL-safe parts, unreadable JSON, duplicate member, unexpected header member, too long.</summary>
    public const string Malformed = "ticket_malformed";

    /// <summary>Header "alg" is not exactly "ES256" ("none" and HMAC included).</summary>
    public const string AlgorithmNotAllowed = "alg_not_allowed";

    /// <summary>Header "typ" is not exactly "eudoria-entry+jwt" (an access token, for example).</summary>
    public const string TypeNotAllowed = "typ_not_allowed";

    /// <summary>No published key has this "kid", even after one new download.</summary>
    public const string KeyUnknown = "kid_unknown";

    /// <summary>The ES256 signature does not match (or is not 64 bytes).</summary>
    public const string SignatureInvalid = "signature_invalid";

    /// <summary>"iss" is not "eudoria-core".</summary>
    public const string IssuerInvalid = "iss_invalid";

    /// <summary>"aud" is not exactly this world's audience (a list is refused too).</summary>
    public const string AudienceInvalid = "aud_invalid";

    /// <summary>"sub", "iat", "exp" or "jti" missing or of the wrong form, or exp not after iat.</summary>
    public const string ClaimsInvalid = "claims_invalid";

    /// <summary>More than the clock tolerance past "exp".</summary>
    public const string Expired = "ticket_expired";

    /// <summary>"iat" more than the clock tolerance in the future.</summary>
    public const string IssuedInTheFuture = "ticket_from_future";

    /// <summary>This "jti" was already accepted.</summary>
    public const string Replayed = "ticket_replayed";

    /// <summary>The memory of accepted "jti" is full: refused rather than risking a replay.</summary>
    public const string ReplayCacheFull = "replay_cache_full";

    /// <summary>No key could ever be downloaded from the core.</summary>
    public const string KeysUnavailable = "keys_unavailable";
}

/// <summary>The outcome of a verification: the account's identifier, or the reason of the refusal.</summary>
public sealed record TicketVerdict(Guid? AccountId, string? Rejection)
{
    public bool IsAccepted => AccountId is not null;

    public static TicketVerdict Accepted(Guid accountId) => new(accountId, null);

    public static TicketVerdict Refused(string rejection) => new(null, rejection);

    /// <summary>"accepted" or the rejection code (the "expected" value of the shared test vectors).</summary>
    public override string ToString() => IsAccepted ? "accepted" : Rejection!;
}
