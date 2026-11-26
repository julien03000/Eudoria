using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Eudoria.Core.Identity.Tokens;

/// <summary>How a signing key's "kid" (key identifier) is derived from its public key.</summary>
internal enum KeyIdStyle
{
    /// <summary>First 16 characters of the Base64 URL-safe SHA-256 of the public key (access tokens, plan 1.3).</summary>
    PublicKeyHashPrefix,

    /// <summary>RFC 7638 JWK thumbprint: anyone holding the published public key can recompute it (entry tickets, plan 1.6).</summary>
    Rfc7638Thumbprint,
}

/// <summary>
/// One of the core's ES256 signing keys (ECDSA P-256). The private part signs tokens; the public part
/// verifies them. Its "kid" (key identifier) is derived from the public key, so a future key
/// rotation can tell keys apart without breaking tokens in flight.
/// </summary>
internal sealed class TokenSigningKey : IDisposable
{
    private readonly ECDsa _key;

    private TokenSigningKey(ECDsa key, KeyIdStyle keyIdStyle)
    {
        _key = key;
        ECParameters publicKey = key.ExportParameters(includePrivateParameters: false);
        PublicX = Base64Url.EncodeToString(publicKey.Q.X);
        PublicY = Base64Url.EncodeToString(publicKey.Q.Y);
        string keyId = keyIdStyle == KeyIdStyle.Rfc7638Thumbprint
            ? Rfc7638Thumbprint(PublicX, PublicY)
            : Base64Url.EncodeToString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];
        SecurityKey = new ECDsaSecurityKey(key)
        {
            KeyId = keyId,
            // IdentityModel's shared cache is keyed by the public key, not by this object: it would hand a
            // later instance of the same key (e.g. after a restart in-process) a provider whose ECDsa was disposed.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
    }

    public ECDsaSecurityKey SecurityKey { get; }

    /// <summary>X coordinate of the public key (32 bytes), Base64 URL-safe without padding, as in a JWK.</summary>
    public string PublicX { get; }

    /// <summary>Y coordinate of the public key (32 bytes), Base64 URL-safe without padding, as in a JWK.</summary>
    public string PublicY { get; }

    /// <summary>Reads a Base64 PKCS#8 P-256 private key. Returns null when it is not one (no detail: it is a secret).</summary>
    public static TokenSigningKey? TryCreate(string base64Pkcs8, KeyIdStyle keyIdStyle = KeyIdStyle.PublicKeyHashPrefix)
    {
        byte[] bytes = new byte[base64Pkcs8.Length];
        if (string.IsNullOrWhiteSpace(base64Pkcs8) || !Convert.TryFromBase64String(base64Pkcs8.Trim(), bytes, out int length))
        {
            return null;
        }

        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(bytes.AsSpan(0, length), out int read);
            if (read == length && key.KeySize == 256)
            {
                return new TokenSigningKey(key, keyIdStyle);
            }
        }
        catch (CryptographicException)
        {
            // not a PKCS#8 ECDSA private key
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        key.Dispose();
        return null;
    }

    public static bool IsValid(string base64Pkcs8)
    {
        using TokenSigningKey? key = TryCreate(base64Pkcs8);
        return key is not null;
    }

    /// <summary>True when both values are valid keys with the same public key (whatever their encoding).</summary>
    public static bool AreSameKey(string base64Pkcs8, string otherBase64Pkcs8)
    {
        using TokenSigningKey? key = TryCreate(base64Pkcs8);
        using TokenSigningKey? other = TryCreate(otherBase64Pkcs8);
        return key is not null && other is not null && key.PublicX == other.PublicX && key.PublicY == other.PublicY;
    }

    /// <summary>
    /// RFC 7638 thumbprint of an EC P-256 public key: SHA-256 of the JSON object with exactly the
    /// required members in lexicographic order and no whitespace, Base64 URL-safe without padding.
    /// </summary>
    public static string Rfc7638Thumbprint(string x, string y) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes($$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""")));

    public void Dispose() => _key.Dispose();
}
