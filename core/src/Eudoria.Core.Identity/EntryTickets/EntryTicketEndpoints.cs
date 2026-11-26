using System.Security.Claims;
using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Eudoria.Core.Identity.EntryTickets;

/// <summary>An issued entry ticket (contract schema EntryTicket).</summary>
internal sealed record EntryTicketResponse(string Ticket, long ExpiresIn);

/// <summary>
/// POST /v1/worlds/{worldId}/entry-tickets and GET /v1/entry-tickets/keys
/// (contract: shared/api/core-v1.yaml, operations issueEntryTicket and getEntryTicketKeys).
/// Never logs a ticket.
/// </summary>
internal static partial class EntryTicketEndpoints
{
    public const string JwkSetContentType = "application/jwk-set+json";

    /// <summary>Same as the world servers' refresh interval (plan 1.6, section 6).</summary>
    public const string KeysCacheControl = "public, max-age=300";

    public static IResult Issue(
        string worldId,
        ClaimsPrincipal user,
        HttpContext context,
        IKnownWorlds worlds,
        EntryTicketIssuer issuer,
        TimeProvider time,
        ILogger<EntryTicketIssuer> logger)
    {
        // "sub" is kept as is: inbound claim mapping is off (owner decision, plan 1.3).
        if (!Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out Guid accountId))
        {
            return TypedResults.Challenge();
        }

        if (!WorldIds.IsValid(worldId))
        {
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "Bad Request",
                [new FieldError("worldId", AccountRules.ErrorCodes.WorldIdInvalid)]);
        }

        if (!worlds.Exists(worldId))
        {
            return ProblemResults.Problem(StatusCodes.Status404NotFound, "Not Found",
                [new FieldError("worldId", AccountRules.ErrorCodes.WorldUnknown)]);
        }

        // Whole seconds: "iat" and "exp" are whole seconds anyway.
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        string ticket = issuer.Issue(accountId, worldId, now);
        LogTicketIssued(logger, accountId, worldId);

        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Json(new EntryTicketResponse(ticket, (long)issuer.Lifetime.TotalSeconds));
    }

    public static IResult GetKeys(HttpContext context, EntryTicketSigningKey signingKey)
    {
        context.Response.Headers.CacheControl = KeysCacheControl;
        return TypedResults.Bytes(signingKey.JwkSetJson, JwkSetContentType);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Entry ticket issued for account {AccountId} to world {WorldId}.")]
    private static partial void LogTicketIssued(ILogger logger, Guid accountId, string worldId);
}
