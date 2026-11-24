using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Eudoria.Core.Identity.Tokens;

/// <summary>
/// The core's ES256 signing key (ECDSA P-256). The private part signs access tokens; the public part
/// verifies them. Its "kid" (key identifier) is derived from the public key, so a future key
/// rotation can tell keys apart without breaking tokens in flight.
/// </summary>
internal sealed class TokenSigningKey : IDisposable
{
    private readonly ECDsa _key;

    private TokenSigningKey(ECDsa key)
    {
        _key = key;
        byte[] publicKeyHash = SHA256.HashData(key.ExportSubjectPublicKeyInfo());
        SecurityKey = new ECDsaSecurityKey(key) { KeyId = Base64Url.EncodeToString(publicKeyHash)[..16] };
    }

    public ECDsaSecurityKey SecurityKey { get; }

    /// <summary>Reads a Base64 PKCS#8 P-256 private key. Returns null when it is not one (no detail: it is a secret).</summary>
    public static TokenSigningKey? TryCreate(string base64Pkcs8)
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
                return new TokenSigningKey(key);
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

    public void Dispose() => _key.Dispose();
}
