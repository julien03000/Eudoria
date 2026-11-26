using System.Security.Claims;
using Eudoria.Core.Identity.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Eudoria.Core.Identity.EntryTickets;

/// <summary>
/// Issues world entry tickets (ADR-0007): JWT signed with ES256 by the entry ticket key, header
/// {alg, kid, typ: eudoria-entry+jwt}, claims exactly iss, sub (account UUID), aud
/// ("eudoria-world:&lt;id&gt;", one value), iat, exp, jti (random UUID) — nothing else.
/// </summary>
internal sealed class EntryTicketIssuer(EntryTicketSigningKey signingKey, IOptions<EntryTicketOptions> options, IOptions<TokenOptions> tokens)
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly SigningCredentials _credentials = new(signingKey.Key.SecurityKey, SecurityAlgorithms.EcdsaSha256);

    public TimeSpan Lifetime => options.Value.Lifetime;

    public string Issue(Guid accountId, string worldId, DateTimeOffset now) =>
        _handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = TokenTypes.EntryTicket,
            Issuer = tokens.Value.Issuer,
            Audience = EntryTicketOptions.AudiencePrefix + worldId,
            IssuedAt = now.UtcDateTime,
            Expires = (now + options.Value.Lifetime).UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
                // Version 4 UUID: 122 random bits (single use, checked by the world).
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = _credentials,
        });
}
