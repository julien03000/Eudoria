using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Eudoria.Core.Api.Health;

/// <summary>Liveness and readiness routes (contract: shared/api/core-v1.yaml, tag "health").</summary>
internal static class HealthEndpoints
{
    /// <summary>Health checks carrying this tag must pass for the core to be "ready".</summary>
    public const string ReadinessTag = "ready";

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder health = api.MapGroup("/health");
        health.MapGet("/live", GetLive);
        health.MapGet("/ready", GetReadyAsync);
        return api;
    }

    // Liveness never touches the database: it only proves the process answers.
    private static Ok<HealthStatusResponse> GetLive() => TypedResults.Ok(HealthStatusResponse.Ok);

    private static async Task<Results<Ok<HealthStatusResponse>, JsonHttpResult<HealthStatusResponse>>> GetReadyAsync(
        HealthCheckService healthChecks, CancellationToken cancellationToken)
    {
        HealthReport report = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(ReadinessTag), cancellationToken);

        if (report.Status == HealthStatus.Healthy)
        {
            return TypedResults.Ok(HealthStatusResponse.Ok);
        }

        return TypedResults.Json(HealthStatusResponse.Unavailable, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

/// <summary>Body of the health routes: <c>{"status":"ok"}</c> or <c>{"status":"unavailable"}</c>.</summary>
internal sealed record HealthStatusResponse(string Status)
{
    public static readonly HealthStatusResponse Ok = new("ok");
    public static readonly HealthStatusResponse Unavailable = new("unavailable");
}
