using System.Security.Cryptography;
using Eudoria.Core.Identity.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real core in memory, with the given database connection string and a throwaway
/// signing key (<see cref="TestSigningKey"/>). Optionally captures every log entry and lets a test
/// replace services (e.g. wrap the password hasher) or the signing key.
/// </summary>
public sealed class CoreApiFactory(
    string connectionString,
    CapturingLoggerProvider? logs = null,
    Action<IServiceCollection>? configureServices = null,
    string? signingKey = null) : WebApplicationFactory<Program>
{
    /// <summary>A connection string to a port where nothing listens: the database is "down".</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Core", connectionString);
        builder.UseSetting("Identity:Tokens:SigningKey", signingKey ?? TestSigningKey.Base64);
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

/// <summary>A P-256 signing key generated in memory for this test run only, shared by every test host.</summary>
internal static class TestSigningKey
{
    public static readonly string Base64 = NewBase64Key(ECCurve.NamedCurves.nistP256);

    /// <summary>The same key as the core under test sees it (same "kid"), to forge tokens in tests.</summary>
    public static readonly TokenSigningKey Key = TokenSigningKey.TryCreate(Base64)!;

    public static string NewBase64Key(ECCurve curve)
    {
        using ECDsa key = ECDsa.Create(curve);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }
}
