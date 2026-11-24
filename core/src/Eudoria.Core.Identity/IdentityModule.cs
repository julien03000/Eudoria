using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Registration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Eudoria.Core.Identity;

/// <summary>
/// Entry point of the Identity module (accounts). The host only calls these two methods;
/// everything else in the module is internal.
/// </summary>
public static class IdentityModule
{
    /// <summary>
    /// Registers the module's services. Requires the host to register the shared
    /// <see cref="NpgsqlDataSource"/> (one connection pool for the whole core).
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>(), ConfigureNpgsql));
        services.AddSingleton(Argon2idOptions.Default);
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RegistrationService>();
        return services;
    }

    /// <summary>Maps the module's public routes under the given version group (e.g. /v1).</summary>
    public static RouteGroupBuilder MapIdentityEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/accounts", RegistrationEndpoint.RegisterAsync);
        return api;
    }

    /// <summary>The module keeps its migration history in its own schema.</summary>
    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTable, IdentityDbContext.Schema);
}
