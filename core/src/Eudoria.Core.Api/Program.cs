using Eudoria.Core.Api.Database;
using Eudoria.Core.Api.Health;
using Eudoria.Core.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCoreDatabase();
builder.Services.AddIdentityModule();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadinessTag]);

WebApplication app = builder.Build();

// Request rate limits per client address (IPv4, or IPv6 /64) on the authentication routes.
app.UseRateLimiter();

// Access tokens (Bearer, ES256) are checked before any protected route runs.
app.UseAuthentication();
app.UseAuthorization();

// Public API, major version 1 (contract: shared/api/core-v1.yaml).
RouteGroupBuilder v1 = app.MapGroup("/v1");
v1.MapHealthEndpoints();
v1.MapIdentityEndpoints();

app.Run();

/// <summary>Entry point; exposed so integration tests can host the core in memory.</summary>
public partial class Program;
