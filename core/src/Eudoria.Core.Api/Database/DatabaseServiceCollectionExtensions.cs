using Microsoft.Extensions.Options;
using Npgsql;

namespace Eudoria.Core.Api.Database;

internal static class DatabaseServiceCollectionExtensions
{
    internal const string MissingConnectionStringMessage =
        "Missing connection string 'ConnectionStrings:Core'. " +
        "Run the core with tools/dev/run-core.ps1, which builds it from the .env file.";

    /// <summary>
    /// Registers the PostgreSQL data source. The core refuses to start when the connection
    /// string is missing, instead of failing later with an obscure error.
    /// </summary>
    public static IServiceCollection AddCoreDatabase(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), MissingConnectionStringMessage)
            .ValidateOnStart();

        // One pooled data source for the whole process; disposed by the container on shutdown.
        services.AddSingleton(serviceProvider =>
            NpgsqlDataSource.Create(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

        return services;
    }
}
