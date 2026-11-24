using System.Buffers.Text;
using System.Security.Cryptography;
using Eudoria.Core.Identity.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Eudoria.Core.Identity.Tests;

public sealed class TokenTests
{
    private static readonly string KeyBase64 = NewKey(ECCurve.NamedCurves.nistP256);

    // ---------- Signing key ----------

    [Fact]
    public void SigningKey_AcceptsAP256Pkcs8Key_WithAStableKeyIdDerivedFromThePublicKey()
    {
        using TokenSigningKey first = TokenSigningKey.TryCreate(KeyBase64)!;
        using TokenSigningKey second = TokenSigningKey.TryCreate(KeyBase64)!;
        using TokenSigningKey other = TokenSigningKey.TryCreate(NewKey(ECCurve.NamedCurves.nistP256))!;

        Assert.Matches("^[A-Za-z0-9_-]{16}$", first.SecurityKey.KeyId);
        Assert.Equal(first.SecurityKey.KeyId, second.SecurityKey.KeyId);
        Assert.NotEqual(first.SecurityKey.KeyId, other.SecurityKey.KeyId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void SigningKey_RejectsAnythingElse(string value) => Assert.Null(TokenSigningKey.TryCreate(value));

    [Fact]
    public void SigningKey_RejectsOtherCurvesAndRsa()
    {
        using RSA rsa = RSA.Create(2048);

        Assert.Null(TokenSigningKey.TryCreate(NewKey(ECCurve.NamedCurves.nistP384)));
        Assert.Null(TokenSigningKey.TryCreate(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())));
    }

    // ---------- Access token ----------

    [Fact]
    public void AccessToken_IsAnEs256Jwt_WithExactlyTheExpectedClaims()
    {
        using TokenSigningKey key = TokenSigningKey.TryCreate(KeyBase64)!;
        AccessTokenIssuer issuer = new(key, Options.Create(new TokenOptions
        {
            Issuer = "eudoria-core",
            Audience = "eudoria-core",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        Guid accountId = Guid.CreateVersion7();
        DateTimeOffset now = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

        JsonWebToken token = new(issuer.Issue(accountId, now));

        Assert.Equal("ES256", token.Alg);
        Assert.Equal("JWT", token.Typ);
        Assert.Equal(key.SecurityKey.KeyId, token.Kid);
        Assert.Equal(["aud", "exp", "iat", "iss", "jti", "sub"], token.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal));
        Assert.Equal(accountId.ToString(), token.Subject);
        Assert.Equal("eudoria-core", token.Issuer);
        Assert.Equal("eudoria-core", Assert.Single(token.Audiences));
        Assert.Equal(now.UtcDateTime, token.IssuedAt);
        Assert.Equal(now.AddMinutes(15).UtcDateTime, token.ValidTo);
        Assert.True(Guid.TryParse(token.Id, out _));
        Assert.NotEqual(token.Id, new JsonWebToken(issuer.Issue(accountId, now)).Id);
    }

    // ---------- Refresh token ----------

    [Fact]
    public void RefreshToken_Is32RandomBytes_InBase64Url_AndItsHashCanBeRecomputed()
    {
        (string token, byte[] hash) = RefreshTokenValue.Generate();
        (string other, _) = RefreshTokenValue.Generate();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.NotEqual(token, other);
        Assert.Equal(SHA256.HashData(Base64Url.DecodeFromChars(token)), hash);
        Assert.Equal(hash, RefreshTokenValue.TryHash(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 42 characters
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 characters
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // standard Base64, not URL-safe
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // padding
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    public void RefreshToken_ThatCannotBeOneOfOurs_HasNoHash(string? token) => Assert.Null(RefreshTokenValue.TryHash(token));

    private static string NewKey(ECCurve curve)
    {
        using ECDsa key = ECDsa.Create(curve);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }
}
