using Eudoria.Core.Identity.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Eudoria.Core.Identity.Registration;

/// <summary>Raw registration fields; null when absent (or JSON null).</summary>
internal sealed record RegistrationRequest(string? Email, string? Pseudo, string? Password);

/// <summary>POST /v1/accounts (contract: shared/api/core-v1.yaml, operation registerAccount).</summary>
internal static class RegistrationEndpoint
{
    private static readonly HashSet<string> Fields = new(["email", "pseudo", "password"], StringComparer.Ordinal);

    public static async Task<JsonHttpResult<object>> RegisterAsync(
        HttpRequest request, RegistrationService registration, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?>? fields = await StrictJsonReader.ReadStringFieldsAsync(request, Fields, cancellationToken);
        if (fields is null)
        {
            return ProblemResults.BodyInvalid();
        }

        RegistrationRequest body = new(
            fields.GetValueOrDefault("email"), fields.GetValueOrDefault("pseudo"), fields.GetValueOrDefault("password"));
        RegistrationResult result = await registration.RegisterAsync(body, cancellationToken);
        return result.Outcome switch
        {
            RegistrationOutcome.Accepted => TypedResults.Json<object>(
                new RegistrationAccepted("accepted"), statusCode: StatusCodes.Status202Accepted),
            RegistrationOutcome.PseudoTaken => ProblemResults.Problem(StatusCodes.Status409Conflict, "Conflict", result.Errors),
            _ => ProblemResults.Problem(StatusCodes.Status400BadRequest, "Bad Request", result.Errors),
        };
    }
}

/// <summary>Body of the 202 response: <c>{"status":"accepted"}</c>.</summary>
internal sealed record RegistrationAccepted(string Status);
