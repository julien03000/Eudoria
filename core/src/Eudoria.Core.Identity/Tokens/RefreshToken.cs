using System.Buffers.Text;
using System.Security.Cryptography;

namespace Eudoria.Core.Identity.Tokens;

/// <summary>
/// One refresh token, as stored by the core: only its SHA-256 hash, never its value.
/// All the tokens issued from one login share a <see cref="FamilyId"/> (= one session on one device).
/// </summary>
internal sealed class RefreshToken
{
    public Guid Id { get; init; }

    public Guid AccountId { get; init; }

    /// <summary>The session: every rotation of one login. Revoked as a whole on logout or theft.</summary>
    public Guid FamilyId { get; init; }

    /// <summary>SHA-256 of the token's 32 random bytes.</summary>
    public required byte[] TokenHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>End of validity of this token.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>End of the session, whatever the renewals.</summary>
    public DateTimeOffset SessionExpiresAt { get; init; }

    /// <summary>Set when consumed (rotation), on logout, or when the session is revoked for reuse.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}

/// <summary>Creates refresh token values and computes their hashes.</summary>
internal static class RefreshTokenValue
{
    public const int Bytes = 32;

    /// <summary>A new token: 32 random bytes, Base64 URL-safe without padding (43 characters), and its hash.</summary>
    public static (string Token, byte[] Hash) Generate()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(Bytes);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>The hash of a token presented by a client, or null if it cannot be one of ours (no database lookup needed).</summary>
    public static byte[]? TryHash(string? token)
    {
        if (token is null || token.Length != 43)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[Bytes];
        return Base64Url.IsValid(token) && Base64Url.TryDecodeFromChars(token, bytes, out int written) && written == Bytes
            ? SHA256.HashData(bytes)
            : null;
    }
}
