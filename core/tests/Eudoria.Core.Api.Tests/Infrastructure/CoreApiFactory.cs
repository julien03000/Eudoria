using System.Net;
using System.Security.Cryptography;
using Eudoria.Core.Identity.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real core in memory, with the given database connection string and a throwaway
/// signing key (<see cref="TestSigningKey"/>). Optionally captures every log entry and lets a test
/// replace services (e.g. wrap the password hasher), the signing key or any setting.
/// The per-address request limits are raised (tests send many requests from one client) unless
/// <paramref name="settings"/> sets them; a request can claim a client address with the
/// <see cref="ClientAddressHeader"/> header (the in-memory host has none).
/// </summary>
public sealed class CoreApiFactory(
    string connectionString,
    CapturingLoggerProvider? logs = null,
    Action<IServiceCollection>? configureServices = null,
    string? signingKey = null,
    IReadOnlyDictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    /// <summary>A connection string to a port where nothing listens: the database is "down".</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    /// <summary>Test-only header: the client address the core should see for this request.</summary>
    public const string ClientAddressHeader = "X-Test-Client-Address";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Core", connectionString);
        builder.UseSetting("Identity:Tokens:SigningKey", signingKey ?? TestSigningKey.Base64);
        foreach (string route in new[] { "Login", "Registration", "Refresh", "Account" })
        {
            builder.UseSetting($"Identity:Throttling:{route}:PermitLimit", "100000");
        }

        foreach ((string key, string value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>());
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

/// <summary>Sets the connection's remote address from <see cref="CoreApiFactory.ClientAddressHeader"/>, before anything else runs.</summary>
internal sealed class ClientAddressStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[CoreApiFactory.ClientAddressHeader], out IPAddress? address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
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
