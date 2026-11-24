using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>Hosts the real core in memory, with the given database connection string.</summary>
public sealed class CoreApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>A connection string to a port where nothing listens: the database is "down".</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Core", connectionString);
    }
}
