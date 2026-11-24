using System.Globalization;
using Eudoria.Core.Identity.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Eudoria.Core.Identity.Http;

/// <summary>One error of the contract's Problem.errors list.</summary>
internal sealed record FieldError(string Field, string Code);

/// <summary>RFC 9457 problem details, with one machine-readable code per error.</summary>
internal sealed record Problem(string Type, string Title, int Status, IReadOnlyList<FieldError> Errors);

/// <summary>The contract's error responses (application/problem+json).</summary>
internal static class ProblemResults
{
    private const string ProblemContentType = "application/problem+json";

    public static JsonHttpResult<object> Problem(int status, string title, IReadOnlyList<FieldError> errors) =>
        TypedResults.Json<object>(new Problem("about:blank", title, status, errors), contentType: ProblemContentType, statusCode: status);

    /// <summary>400 for a body that cannot be read at all.</summary>
    public static JsonHttpResult<object> BodyInvalid() =>
        Problem(StatusCodes.Status400BadRequest, "Bad Request", [new FieldError("body", AccountRules.ErrorCodes.BodyInvalid)]);

    /// <summary>429 too_many_attempts, with Retry-After (whole seconds, rounded up, at least 1).</summary>
    public static JsonHttpResult<object> TooManyRequests(HttpContext context, TimeSpan retryAfter)
    {
        SetRetryAfter(context, retryAfter);
        return Problem(StatusCodes.Status429TooManyRequests, "Too Many Requests",
            [new FieldError("request", AccountRules.ErrorCodes.TooManyAttempts)]);
    }

    /// <summary>503 server_busy, with Retry-After: no password hashing slot became free in time.</summary>
    public static JsonHttpResult<object> ServerBusy(HttpContext context)
    {
        SetRetryAfter(context, TimeSpan.FromSeconds(1));
        return Problem(StatusCodes.Status503ServiceUnavailable, "Service Unavailable",
            [new FieldError("request", AccountRules.ErrorCodes.ServerBusy)]);
    }

    private static void SetRetryAfter(HttpContext context, TimeSpan retryAfter)
    {
        long seconds = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
    }
}
