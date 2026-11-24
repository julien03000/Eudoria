using Eudoria.Core.Api.Database;
using Eudoria.Core.Api.Health;
using Eudoria.Core.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCoreDatabase();
builder.Services.AddIdentityModule();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadinessTag]);

WebApplication app = builder.Build();

// Public API, major version 1 (contract: shared/api/core-v1.yaml).
RouteGroupBuilder v1 = app.MapGroup("/v1");
v1.MapHealthEndpoints();
v1.MapIdentityEndpoints();

app.Run();

/// <summary>Entry point; exposed so integration tests can host the core in memory.</summary>
public partial class Program;
