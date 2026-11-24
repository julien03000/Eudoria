using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real core in memory, with the given database connection string. Optionally captures
/// every log entry and lets a test replace services (e.g. wrap the password hasher).
/// </summary>
public sealed class CoreApiFactory(
    string connectionString,
    CapturingLoggerProvider? logs = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    /// <summary>A connection string to a port where nothing listens: the database is "down".</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Core", connectionString);
        if (logs is not null)
        {
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
