using System.Security.Claims;
using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Http;
using Eudoria.Core.Identity.Sessions;
using Eudoria.Core.Identity.Throttling;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Eudoria.Core.Identity.AccountSecurity;

/// <summary>
/// POST /v1/me/password and POST /v1/me/sessions/revoke-all (contract: shared/api/core-v1.yaml,
/// operations changeMyPassword and revokeAllMySessions). Both require a valid access token.
/// </summary>
internal static class AccountSecurityEndpoints
{
    private static readonly HashSet<string> PasswordChangeFields = new(["currentPassword", "newPassword"], StringComparer.Ordinal);

    public static async Task<IResult> ChangePasswordAsync(
        HttpContext context, ClaimsPrincipal user, PasswordChangeService passwords, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?>? fields = await StrictJsonReader.ReadStringFieldsAsync(context.Request, PasswordChangeFields, cancellationToken);
        if (fields is null)
        {
            return ProblemResults.BodyInvalid();
        }

        if (!TryGetAccountId(user, out Guid accountId))
        {
            return TypedResults.Challenge();
        }

        PasswordChangeResult result = await passwords.ChangeAsync(
            accountId, fields.GetValueOrDefault("currentPassword"), fields.GetValueOrDefault("newPassword"),
            ClientAddress.Key(context.Connection.RemoteIpAddress), cancellationToken);
        return result.Outcome switch
        {
            PasswordChangeOutcome.Changed => SessionEndpoints.Tokens(context, result.Tokens!),
            PasswordChangeOutcome.Invalid => ProblemResults.Problem(StatusCodes.Status400BadRequest, "Bad Request", result.Errors!),
            PasswordChangeOutcome.WrongCurrentPassword => ProblemResults.Problem(StatusCodes.Status401Unauthorized, "Unauthorized",
                [new FieldError("currentPassword", AccountRules.ErrorCodes.InvalidCredentials)]),
            PasswordChangeOutcome.Throttled => ProblemResults.TooManyRequests(context, result.RetryAfter!.Value),
            _ => TypedResults.Challenge(),
        };
    }

    public static async Task<IResult> RevokeAllSessionsAsync(ClaimsPrincipal user, SessionService sessions, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(user, out Guid accountId) || !await sessions.RevokeAllAsync(accountId, cancellationToken))
        {
            return TypedResults.Challenge();
        }

        return TypedResults.NoContent();
    }

    // "sub" is kept as is: inbound claim mapping is off (owner decision, plan 1.3).
    private static bool TryGetAccountId(ClaimsPrincipal user, out Guid accountId) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out accountId);
}
