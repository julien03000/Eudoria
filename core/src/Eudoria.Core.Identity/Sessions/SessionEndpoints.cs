using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Http;
using Eudoria.Core.Identity.Throttling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Eudoria.Core.Identity.Sessions;

/// <summary>
/// POST /v1/auth/login, /v1/auth/refresh and /v1/auth/logout
/// (contract: shared/api/core-v1.yaml, operations login, refreshSession, logout).
/// </summary>
internal static class SessionEndpoints
{
    private static readonly HashSet<string> LoginFields = new(["email", "password"], StringComparer.Ordinal);
    private static readonly HashSet<string> RefreshTokenFields = new(["refreshToken"], StringComparer.Ordinal);

    public static async Task<IResult> LoginAsync(HttpContext context, SessionService sessions, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?>? fields = await StrictJsonReader.ReadStringFieldsAsync(context.Request, LoginFields, cancellationToken);
        if (fields is null)
        {
            return ProblemResults.BodyInvalid();
        }

        LoginResult result = await sessions.LoginAsync(
            fields.GetValueOrDefault("email"), fields.GetValueOrDefault("password"),
            ClientAddress.Key(context.Connection.RemoteIpAddress), cancellationToken);
        if (result.RetryAfter is { } retryAfter)
        {
            return ProblemResults.TooManyRequests(context, retryAfter);
        }

        return result.Tokens is null
            ? ProblemResults.Problem(StatusCodes.Status401Unauthorized, "Unauthorized",
                [new FieldError("credentials", AccountRules.ErrorCodes.InvalidCredentials)])
            : Tokens(context, result.Tokens);
    }

    public static async Task<IResult> RefreshAsync(HttpContext context, SessionService sessions, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?>? fields = await StrictJsonReader.ReadStringFieldsAsync(context.Request, RefreshTokenFields, cancellationToken);
        if (fields is null)
        {
            return ProblemResults.BodyInvalid();
        }

        TokenPair? pair = await sessions.RefreshAsync(fields.GetValueOrDefault("refreshToken"), cancellationToken);
        return pair is null
            ? ProblemResults.Problem(StatusCodes.Status401Unauthorized, "Unauthorized",
                [new FieldError("refreshToken", AccountRules.ErrorCodes.RefreshTokenInvalid)])
            : Tokens(context, pair);
    }

    public static async Task<IResult> LogoutAsync(HttpContext context, SessionService sessions, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?>? fields = await StrictJsonReader.ReadStringFieldsAsync(context.Request, RefreshTokenFields, cancellationToken);
        if (fields is null)
        {
            return ProblemResults.BodyInvalid();
        }

        await sessions.LogoutAsync(fields.GetValueOrDefault("refreshToken"), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Token responses must never be cached (RFC 6749, section 5.1).</summary>
    internal static JsonHttpResult<TokenPair> Tokens(HttpContext context, TokenPair pair)
    {
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Json(pair);
    }
}
