using System.Net;
using Eudoria.Core.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Eudoria.Core.Api.Tests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task Live_ReturnsOk_WithoutTouchingTheDatabase()
    {
        await using CoreApiFactory factory = new(CoreApiFactory.UnreachableDatabase);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ready_Returns503WithoutInternalDetails_WhenDatabaseIsUnreachable()
    {
        await using CoreApiFactory factory = new(CoreApiFactory.UnreachableDatabase);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // Exact body: no error message, host name or stack trace may leak.
        Assert.Equal("""{"status":"unavailable"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        await using CoreApiFactory factory = new(CoreApiFactory.UnreachableDatabase);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/health/unknown", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Startup_IsRefused_WhenConnectionStringIsMissing(string connectionString)
    {
        await using CoreApiFactory factory = new(connectionString);

        Exception? exception = Record.Exception(() => factory.CreateClient());

        OptionsValidationException validation = Assert.IsType<OptionsValidationException>(FindInChain<OptionsValidationException>(exception));
        Assert.Contains("ConnectionStrings:Core", validation.Message, StringComparison.Ordinal);
    }

    private static T? FindInChain<T>(Exception? exception)
        where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}

[Collection(PostgresCollectionDefinition.Name)]
public sealed class HealthEndpointsWithDatabaseTests(PostgresFixture database)
{
    [Fact]
    public async Task Ready_ReturnsOk_WhenDatabaseIsReachable()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
