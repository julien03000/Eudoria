namespace Eudoria.Core.Identity.Tokens;

/// <summary>
/// Session and token settings (configuration section <c>Identity:Tokens</c>). Everything but the
/// signing key lives in appsettings.json; the key is a secret, given by the environment variable
/// <c>Identity__Tokens__SigningKey</c> (tools/dev/run-core.ps1 reads it from .env).
/// </summary>
internal sealed class TokenOptions
{
    public const string SectionName = "Identity:Tokens";

    /// <summary>"iss" claim of the access tokens.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>"aud" claim: the access token is accepted by the core API only (owner decision, plan 1.3).</summary>
    public string Audience { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; }

    /// <summary>Validity of one refresh token after it is issued (each renewal issues a new one).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; }

    /// <summary>Maximum duration of a session (refresh token family) after login.</summary>
    public TimeSpan SessionLifetime { get; set; }

    /// <summary>ES256 private key: ECDSA P-256, PKCS#8, Base64. Secret: never logged.</summary>
    public string SigningKey { get; set; } = string.Empty;
}
