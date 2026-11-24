using System.Security.Claims;
using Eudoria.Core.Identity.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Eudoria.Core.Identity.Profile;

/// <summary>The caller's own profile (contract schema Profile). The e-mail is deliberately left out.</summary>
internal sealed record ProfileResponse(Guid Id, string Pseudo, DateTimeOffset CreatedAt);

/// <summary>GET /v1/me (contract: shared/api/core-v1.yaml, operation getMyProfile). Requires a valid access token.</summary>
internal static class MeEndpoint
{
    public static async Task<IResult> GetAsync(ClaimsPrincipal user, IdentityDbContext database, CancellationToken cancellationToken)
    {
        // "sub" is kept as is: inbound claim mapping is off (owner decision, plan 1.3).
        if (!Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out Guid accountId))
        {
            return TypedResults.Challenge();
        }

        ProfileResponse? profile = await database.Accounts.AsNoTracking()
            .Where(account => account.Id == accountId)
            .Select(account => new ProfileResponse(account.Id, account.Pseudo, account.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        // A valid token for an account that no longer exists: same answer as an invalid token.
        return profile is null ? TypedResults.Challenge() : TypedResults.Json(profile);
    }
}
