using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Accounts;
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

    // A pseudo registered before the "409" scenario (Setup), so that the scenario finds it taken.
    private static readonly string TakenPseudo = RegistrationTests.UniquePseudo();

    /// <summary>One scenario per documented (method, path, status). Adding a response to the
    /// contract without a scenario here makes <see cref="EveryDocumentedResponse_HasAScenario"/> fail.</summary>
    private static readonly ContractScenario[] Scenarios =
    [
        new("GET", "/health/live", HttpStatusCode.OK, DatabaseReachable: true),
        new("GET", "/health/ready", HttpStatusCode.OK, DatabaseReachable: true),
        new("GET", "/health/ready", HttpStatusCode.ServiceUnavailable, DatabaseReachable: false),
        new("POST", "/accounts", HttpStatusCode.Accepted, DatabaseReachable: true,
            Body: () => RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), RegistrationTests.UniquePseudo(), "a-long-enough-passphrase")),
        new("POST", "/accounts", HttpStatusCode.BadRequest, DatabaseReachable: true,
            Body: () => RegistrationTests.RegistrationJson("not-an-email", "x", "short")),
        new("POST", "/accounts", HttpStatusCode.Conflict, DatabaseReachable: true,
            Body: () => RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), TakenPseudo, "a-long-enough-passphrase"),
            Setup: () => RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), TakenPseudo, "a-long-enough-passphrase")),
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
            (string mediaType, IOpenApiMediaType content) = Assert.Single(documented.Content!);
            IOpenApiSchema schema = content.Schema!;

            await using CoreApiFactory factory = new(scenario.DatabaseReachable ? database.ConnectionString : CoreApiFactory.UnreachableDatabase);
            using HttpClient client = factory.CreateClient();
            if (scenario.Setup is not null)
            {
                using HttpResponseMessage setup = await SendAsync(client, scenario.Method, basePath + scenario.Path, scenario.Setup());
                Assert.True(setup.IsSuccessStatusCode, $"{scenario}: setup failed with {setup.StatusCode}.");
            }

            using HttpResponseMessage response = await SendAsync(client, scenario.Method, basePath + scenario.Path, scenario.Body?.Invoke());

            Assert.Equal(scenario.Status, response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            JsonNode? body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            IReadOnlyList<string> errors = SchemaValidator.Validate(body, schema);
            Assert.True(errors.Count == 0, $"{scenario}: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public async Task RequestExamples_MatchTheirSchemas()
    {
        OpenApiDocument contract = await LoadDocumentAsync();

        foreach (OpenApiOperation operation in contract.Paths.Values.SelectMany(path => path.Operations?.Values.AsEnumerable() ?? []))
        {
            foreach ((string mediaType, IOpenApiMediaType content) in operation.RequestBody?.Content ?? new Dictionary<string, IOpenApiMediaType>())
            {
                Assert.NotNull(content.Example);
                IReadOnlyList<string> errors = SchemaValidator.Validate(content.Example, content.Schema!);
                Assert.True(errors.Count == 0, $"{operation.OperationId} {mediaType}: {string.Join("; ", errors)}");
            }
        }
    }

    [Fact]
    public async Task RegistrationLimits_InTheContract_MatchTheCode()
    {
        OpenApiDocument contract = await LoadDocumentAsync();
        IDictionary<string, IOpenApiSchema> fields = contract.Components!.Schemas!["RegisterAccountRequest"].Properties!;

        Assert.Equal(AccountRules.EmailMaxLength, fields["email"].MaxLength);
        Assert.Equal(AccountRules.PseudoMinLength, fields["pseudo"].MinLength);
        Assert.Equal(AccountRules.PseudoMaxLength, fields["pseudo"].MaxLength);
        Assert.Equal(AccountRules.PasswordMinLength, fields["password"].MinLength);
        Assert.Equal(AccountRules.PasswordMaxLength, fields["password"].MaxLength);

        IEnumerable<string> documentedCodes = contract.Components.Schemas["FieldError"].Properties!["code"].Enum!.Select(code => code!.GetValue<string>());
        IEnumerable<string> codeConstants = typeof(AccountRules.ErrorCodes).GetFields().Select(field => (string)field.GetRawConstantValue()!);
        Assert.Equal(documentedCodes.Order(StringComparer.Ordinal), codeConstants.Order(StringComparer.Ordinal));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? jsonBody)
    {
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
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

    /// <param name="Body">JSON request body, built when the scenario runs (unique values).</param>
    /// <param name="Setup">Optional JSON body sent once to the same route before the scenario.</param>
    private sealed record ContractScenario(
        string Method, string Path, HttpStatusCode Status, bool DatabaseReachable,
        Func<string>? Body = null, Func<string>? Setup = null);
}
