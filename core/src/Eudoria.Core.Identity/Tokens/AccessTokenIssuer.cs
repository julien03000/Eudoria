using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Eudoria.Core.Identity.Tokens;

/// <summary>
/// Issues access tokens: JWT signed with ES256, header {alg, kid, typ}, claims sub (account UUID),
/// iss, aud, iat, exp, jti — and nothing else (no personal or changeable data).
/// </summary>
internal sealed class AccessTokenIssuer(TokenSigningKey signingKey, IOptions<TokenOptions> options)
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly SigningCredentials _credentials = new(signingKey.SecurityKey, SecurityAlgorithms.EcdsaSha256);

    public TimeSpan Lifetime => options.Value.AccessTokenLifetime;

    public string Issue(Guid accountId, DateTimeOffset now) =>
        _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now.UtcDateTime,
            Expires = (now + options.Value.AccessTokenLifetime).UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = _credentials,
        });
}
