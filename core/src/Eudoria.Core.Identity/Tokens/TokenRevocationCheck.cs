using Eudoria.Core.Identity.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Eudoria.Core.Identity.Tokens;

/// <summary>
/// Runs after the signature, issuer, audience and lifetime checks of every protected call: refuses
/// an access token issued (claim "iat") before the account's "tokens valid after" date, set by a
/// password change or "revoke all sessions" (owner decision, plan 1.4, point 4). One primary-key
/// read per call. Only the core accepts these tokens (audience "eudoria-core"), so world servers,
/// which will check their own entry tickets without a database (1.6), are not concerned.
/// </summary>
internal static class TokenRevocationCheck
{
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        if (context.SecurityToken is not JsonWebToken token || !Guid.TryParse(token.Subject, out Guid accountId))
        {
            context.Fail("Access token without a valid subject.");
            return;
        }

        IdentityDbContext database = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var account = await database.Accounts.AsNoTracking()
            .Where(candidate => candidate.Id == accountId)
            .Select(candidate => new { candidate.TokensValidAfter })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (account is null)
        {
            context.Fail("Access token of an account that no longer exists.");
        }
        else if (account.TokensValidAfter is { } validAfter && token.IssuedAt < validAfter.UtcDateTime)
        {
            context.Fail("Access token issued before the account's sessions were revoked.");
        }
    }
}
