using Eudoria.Core.Identity.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Eudoria.Core.Identity.Registration;

/// <summary>POST /v1/accounts (contract: shared/api/core-v1.yaml, operation registerAccount).</summary>
internal static class RegistrationEndpoint
{
    private const string ProblemContentType = "application/problem+json";

    public static async Task<JsonHttpResult<object>> RegisterAsync(
        HttpRequest request, RegistrationService registration, CancellationToken cancellationToken)
    {
        RegistrationRequest? body = await RegistrationRequestReader.ReadAsync(request, cancellationToken);
        if (body is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "Bad Request",
                [new FieldError("body", AccountRules.ErrorCodes.BodyInvalid)]);
        }

        RegistrationResult result = await registration.RegisterAsync(body, cancellationToken);
        return result.Outcome switch
        {
            RegistrationOutcome.Accepted => TypedResults.Json<object>(
                new RegistrationAccepted("accepted"), statusCode: StatusCodes.Status202Accepted),
            RegistrationOutcome.PseudoTaken => Problem(StatusCodes.Status409Conflict, "Conflict", result.Errors),
            _ => Problem(StatusCodes.Status400BadRequest, "Bad Request", result.Errors),
        };
    }

    private static JsonHttpResult<object> Problem(int status, string title, IReadOnlyList<FieldError> errors) =>
        TypedResults.Json<object>(new Problem("about:blank", title, status, errors), contentType: ProblemContentType, statusCode: status);
}

/// <summary>Body of the 202 response: <c>{"status":"accepted"}</c>.</summary>
internal sealed record RegistrationAccepted(string Status);

/// <summary>RFC 9457 problem details, with one machine-readable code per error.</summary>
internal sealed record Problem(string Type, string Title, int Status, IReadOnlyList<FieldError> Errors);
