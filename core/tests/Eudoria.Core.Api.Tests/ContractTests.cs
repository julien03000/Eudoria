using System.Net;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Eudoria.Core.Api.Tests;

/// <summary>
/// Checks the core against its public contract (shared/api/core-v1.yaml), in both directions:
/// everything documented exists and behaves as documented, and nothing undocumented is exposed.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ContractTests(PostgresFixture database)
{
    private static readonly string ContractPath = Path.Combine(AppContext.BaseDirectory, "Contracts", "core-v1.yaml");

    /// <summary>One scenario per documented (method, path, status). Adding a response to the
    /// contract without a scenario here makes <see cref="EveryDocumentedResponse_HasAScenario"/> fail.</summary>
    private static readonly ContractScenario[] Scenarios =
    [
        new("GET", "/health/live", HttpStatusCode.OK, DatabaseReachable: true),
        new("GET", "/health/ready", HttpStatusCode.OK, DatabaseReachable: true),
        new("GET", "/health/ready", HttpStatusCode.ServiceUnavailable, DatabaseReachable: false),
    ];

    [Fact]
    public async Task Contract_IsValidOpenApi()
    {
        ReadResult result = await LoadContractAsync();

        Assert.NotNull(result.Document);
        Assert.Empty(result.Diagnostic?.Errors ?? []);
    }

    [Fact]
    public async Task EveryDocumentedResponse_HasAScenario()
    {
        OpenApiDocument contract = await LoadDocumentAsync();

        string[] documented = [.. EnumerateOperations(contract)
            .SelectMany(operation => operation.Responses.Keys.Select(status => $"{operation.Method} {operation.Path} {status}"))
            .Order(StringComparer.Ordinal)];
        string[] covered = [.. Scenarios
            .Select(scenario => $"{scenario.Method} {scenario.Path} {(int)scenario.Status}")
            .Order(StringComparer.Ordinal)];

        Assert.Equal(documented, covered);
    }

    [Fact]
    public async Task ExposedRoutes_MatchDocumentedRoutes()
    {
        OpenApiDocument contract = await LoadDocumentAsync();
        string basePath = GetBasePath(contract);

        await using CoreApiFactory factory = new(CoreApiFactory.UnreachableDatabase);
        using HttpClient client = factory.CreateClient(); // starts the host

        string[] exposed = [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)];
        string[] documented = [.. EnumerateOperations(contract)
            .Select(operation => $"{operation.Method} {basePath}{operation.Path}")
            .Order(StringComparer.Ordinal)];

        Assert.Equal(documented, exposed);
    }

    [Fact]
    public async Task Responses_MatchTheContract()
    {
        OpenApiDocument contract = await LoadDocumentAsync();
        string basePath = GetBasePath(contract);

        foreach (ContractScenario scenario in Scenarios)
        {
            IOpenApiResponse documented = EnumerateOperations(contract)
                .Single(operation => operation.Method == scenario.Method && operation.Path == scenario.Path)
                .Responses[((int)scenario.Status).ToString(System.Globalization.CultureInfo.InvariantCulture)];
            IOpenApiSchema schema = documented.Content!["application/json"].Schema!;

            await using CoreApiFactory factory = new(scenario.DatabaseReachable ? database.ConnectionString : CoreApiFactory.UnreachableDatabase);
            using HttpClient client = factory.CreateClient();
            using HttpRequestMessage request = new(new HttpMethod(scenario.Method), new Uri(basePath + scenario.Path, UriKind.Relative));
            using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(scenario.Status, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            JsonNode? body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            IReadOnlyList<string> errors = SchemaValidator.Validate(body, schema);
            Assert.True(errors.Count == 0, $"{scenario}: {string.Join("; ", errors)}");
        }
    }

    private static async Task<ReadResult> LoadContractAsync()
    {
        OpenApiReaderSettings settings = new();
        settings.AddYamlReader();
        await using FileStream stream = File.OpenRead(ContractPath);
        return await OpenApiDocument.LoadAsync(stream, "yaml", settings, TestContext.Current.CancellationToken);
    }

    private static async Task<OpenApiDocument> LoadDocumentAsync()
    {
        ReadResult result = await LoadContractAsync();
        return result.Document ?? throw new InvalidOperationException($"Cannot read {ContractPath}.");
    }

    /// <summary>The version prefix carried by the server URL, e.g. "/v1".</summary>
    private static string GetBasePath(OpenApiDocument contract) =>
        new Uri(contract.Servers![0].Url!).AbsolutePath.TrimEnd('/');

    private static IEnumerable<(string Method, string Path, OpenApiResponses Responses)> EnumerateOperations(OpenApiDocument contract) =>
        from path in contract.Paths
        from operation in path.Value.Operations ?? []
        select (operation.Key.Method.ToUpperInvariant(), path.Key, operation.Value.Responses ?? []);

    private sealed record ContractScenario(string Method, string Path, HttpStatusCode Status, bool DatabaseReachable);
}
