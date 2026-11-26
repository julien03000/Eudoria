using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Passwords;
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

    // A well-formed refresh token that was never issued.
    private const string UnknownRefreshToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    // Settings that make the next request of a route exceed its per-address limit.
    private static readonly Dictionary<string, string> OneRegistration = new() { ["Identity:Throttling:Registration:PermitLimit"] = "1" };
    private static readonly Dictionary<string, string> OneRefresh = new() { ["Identity:Throttling:Refresh:PermitLimit"] = "1" };
    private static readonly Dictionary<string, string> OneAccountRequest = new() { ["Identity:Throttling:Account:PermitLimit"] = "1" };
    private static readonly Dictionary<string, string> OneEntryTicket = new() { ["Identity:Throttling:EntryTicket:PermitLimit"] = "1" };
    private static readonly Dictionary<string, string> OneKeysDownload = new() { ["Identity:Throttling:TicketKeys:PermitLimit"] = "1" };

    // One hashing slot, a short wait: a request gets 503 while a scenario holds the slot.
    private static readonly Dictionary<string, string> OneHashingSlot = new()
    {
        ["Identity:Throttling:MaxConcurrentHashes"] = "1",
        ["Identity:Throttling:HashQueueTimeout"] = "00:00:00.100",
    };

    /// <summary>One scenario per documented (method, path, status). Adding a response to the
    /// contract without a scenario here makes <see cref="EveryDocumentedResponse_HasAScenario"/> fail.</summary>
    private static readonly ContractScenario[] Scenarios =
    [
        new("GET", "/health/live", HttpStatusCode.OK, DatabaseReachable: true),
        new("GET", "/health/ready", HttpStatusCode.OK, DatabaseReachable: true),
        new("GET", "/health/ready", HttpStatusCode.ServiceUnavailable, DatabaseReachable: false),
        new("POST", "/accounts", HttpStatusCode.Accepted, DatabaseReachable: true,
            Json(() => RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), RegistrationTests.UniquePseudo(), "a-long-enough-passphrase"))),
        new("POST", "/accounts", HttpStatusCode.BadRequest, DatabaseReachable: true,
            Json(() => RegistrationTests.RegistrationJson("not-an-email", "x", "short"))),
        new("POST", "/accounts", HttpStatusCode.Conflict, DatabaseReachable: true, async (client, services) =>
        {
            (_, string takenPseudo) = await SessionTests.RegisterAsync(client);
            return new(RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), takenPseudo, "a-long-enough-passphrase"));
        }),
        new("POST", "/accounts", HttpStatusCode.TooManyRequests, DatabaseReachable: true, async (client, services) =>
        {
            await SessionTests.RegisterAsync(client);
            return new(RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), RegistrationTests.UniquePseudo(), "a-long-enough-passphrase"));
        }, OneRegistration),
        new("POST", "/accounts", HttpStatusCode.ServiceUnavailable, DatabaseReachable: true, async (_, services) => new(
            RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), RegistrationTests.UniquePseudo(), "a-long-enough-passphrase"),
            Held: await TakeEveryHashingSlotAsync(services)), OneHashingSlot),
        new("POST", "/auth/login", HttpStatusCode.OK, DatabaseReachable: true, async (client, services) =>
        {
            (string email, _) = await SessionTests.RegisterAsync(client);
            return new(SessionTests.LoginJson(email, SessionTests.Password));
        }),
        new("POST", "/auth/login", HttpStatusCode.BadRequest, DatabaseReachable: true, Json(() => "not json")),
        new("POST", "/auth/login", HttpStatusCode.Unauthorized, DatabaseReachable: true,
            Json(() => SessionTests.LoginJson(RegistrationTests.UniqueEmail(), SessionTests.Password))),
        new("POST", "/auth/login", HttpStatusCode.TooManyRequests, DatabaseReachable: true, async (client, services) =>
        {
            string email = RegistrationTests.UniqueEmail();
            for (int i = 0; i < 5; i++)
            {
                using HttpResponseMessage wrong = await SessionTests.LoginAsync(client, email, "not the right password");
            }

            return new(SessionTests.LoginJson(email, SessionTests.Password));
        }),
        new("POST", "/auth/login", HttpStatusCode.ServiceUnavailable, DatabaseReachable: true, async (_, services) => new(
            SessionTests.LoginJson(RegistrationTests.UniqueEmail(), SessionTests.Password),
            Held: await TakeEveryHashingSlotAsync(services)), OneHashingSlot),
        new("POST", "/auth/refresh", HttpStatusCode.OK, DatabaseReachable: true, async (client, services) =>
        {
            (string email, _) = await SessionTests.RegisterAsync(client);
            JsonNode tokens = await SessionTests.LoginJsonAsync(client, email);
            return new(SessionTests.RefreshTokenJson(tokens["refreshToken"]!.GetValue<string>()));
        }),
        new("POST", "/auth/refresh", HttpStatusCode.BadRequest, DatabaseReachable: true, Json(() => "not json")),
        new("POST", "/auth/refresh", HttpStatusCode.Unauthorized, DatabaseReachable: true,
            Json(() => SessionTests.RefreshTokenJson(UnknownRefreshToken))),
        new("POST", "/auth/refresh", HttpStatusCode.TooManyRequests, DatabaseReachable: true, async (client, services) =>
        {
            using HttpResponseMessage first = await SessionTests.RefreshAsync(client, UnknownRefreshToken);
            return new(SessionTests.RefreshTokenJson(UnknownRefreshToken));
        }, OneRefresh),
        new("POST", "/auth/logout", HttpStatusCode.NoContent, DatabaseReachable: true,
            Json(() => SessionTests.RefreshTokenJson(UnknownRefreshToken))),
        new("POST", "/auth/logout", HttpStatusCode.BadRequest, DatabaseReachable: true, Json(() => "not json")),
        new("GET", "/me", HttpStatusCode.OK, DatabaseReachable: true, async (client, services) =>
            new(Body: null, BearerToken: await AccessTokenAsync(client))),
        new("GET", "/me", HttpStatusCode.Unauthorized, DatabaseReachable: true),
        new("POST", "/me/password", HttpStatusCode.OK, DatabaseReachable: true, async (client, services) =>
            new(AccountSecurityTests.ChangePasswordJson(SessionTests.Password, AccountSecurityTests.NewPassword), await AccessTokenAsync(client))),
        new("POST", "/me/password", HttpStatusCode.BadRequest, DatabaseReachable: true, async (client, services) =>
            new(AccountSecurityTests.ChangePasswordJson(SessionTests.Password, "azertyuiop123"), await AccessTokenAsync(client))),
        new("POST", "/me/password", HttpStatusCode.Unauthorized, DatabaseReachable: true, async (client, services) =>
            new(AccountSecurityTests.ChangePasswordJson("not the right password", AccountSecurityTests.NewPassword), await AccessTokenAsync(client))),
        new("POST", "/me/password", HttpStatusCode.TooManyRequests, DatabaseReachable: true, async (client, services) =>
        {
            string accessToken = await AccessTokenAsync(client);
            using HttpResponseMessage first = await AccountSecurityTests.ChangePasswordAsync(client, accessToken, "not the right password", AccountSecurityTests.NewPassword);
            return new(AccountSecurityTests.ChangePasswordJson(SessionTests.Password, AccountSecurityTests.NewPassword), accessToken);
        }, OneAccountRequest),
        new("POST", "/me/password", HttpStatusCode.ServiceUnavailable, DatabaseReachable: true, async (client, services) =>
        {
            string accessToken = await AccessTokenAsync(client);
            return new(AccountSecurityTests.ChangePasswordJson(SessionTests.Password, AccountSecurityTests.NewPassword), accessToken,
                Held: await TakeEveryHashingSlotAsync(services));
        }, OneHashingSlot),
        new("POST", "/me/sessions/revoke-all", HttpStatusCode.NoContent, DatabaseReachable: true, async (client, services) =>
            new(Body: null, BearerToken: await AccessTokenAsync(client))),
        new("POST", "/me/sessions/revoke-all", HttpStatusCode.Unauthorized, DatabaseReachable: true),
        new("POST", "/me/sessions/revoke-all", HttpStatusCode.TooManyRequests, DatabaseReachable: true, async (client, services) =>
        {
            using HttpResponseMessage first = await AccountSecurityTests.RevokeAllAsync(client, accessToken: null);
            return new(Body: null, BearerToken: null);
        }, OneAccountRequest),
        new("POST", "/worlds/{worldId}/entry-tickets", HttpStatusCode.OK, DatabaseReachable: true, async (client, services) =>
            new(Body: null, BearerToken: await AccessTokenAsync(client)), RequestPath: "/worlds/sandbox/entry-tickets"),
        new("POST", "/worlds/{worldId}/entry-tickets", HttpStatusCode.BadRequest, DatabaseReachable: true, async (client, services) =>
            new(Body: null, BearerToken: await AccessTokenAsync(client)), RequestPath: "/worlds/Sandbox/entry-tickets"),
        new("POST", "/worlds/{worldId}/entry-tickets", HttpStatusCode.Unauthorized, DatabaseReachable: true,
            RequestPath: "/worlds/sandbox/entry-tickets"),
        new("POST", "/worlds/{worldId}/entry-tickets", HttpStatusCode.NotFound, DatabaseReachable: true, async (client, services) =>
            new(Body: null, BearerToken: await AccessTokenAsync(client)), RequestPath: "/worlds/no-such-world/entry-tickets"),
        new("POST", "/worlds/{worldId}/entry-tickets", HttpStatusCode.TooManyRequests, DatabaseReachable: true, async (client, services) =>
        {
            string accessToken = await AccessTokenAsync(client);
            using HttpResponseMessage first = await EntryTicketTests.RequestTicketAsync(client, accessToken, "sandbox");
            return new(Body: null, BearerToken: accessToken);
        }, OneEntryTicket, RequestPath: "/worlds/sandbox/entry-tickets"),
        new("GET", "/entry-tickets/keys", HttpStatusCode.OK, DatabaseReachable: false),
        new("GET", "/entry-tickets/keys", HttpStatusCode.TooManyRequests, DatabaseReachable: false, async (client, services) =>
        {
            using HttpResponseMessage first = await client.GetAsync(new Uri("/v1/entry-tickets/keys", UriKind.Relative), TestContext.Current.CancellationToken);
            return new(Body: null);
        }, OneKeysDownload),
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

            await using CoreApiFactory factory = new(
                scenario.DatabaseReachable ? database.ConnectionString : CoreApiFactory.UnreachableDatabase, settings: scenario.Settings);
            using HttpClient client = factory.CreateClient();
            ScenarioRequest request = scenario.Prepare is null ? new(Body: null) : await scenario.Prepare(client, factory.Services);

            using HttpResponseMessage response = await SendAsync(client, scenario.Method, basePath + (scenario.RequestPath ?? scenario.Path), request);
            request.Held?.Dispose();

            Assert.Equal(scenario.Status, response.StatusCode);
            foreach ((string name, IOpenApiHeader header) in documented.Headers ?? new Dictionary<string, IOpenApiHeader>())
            {
                Assert.True(response.Headers.TryGetValues(name, out IEnumerable<string>? values), $"{scenario}: missing header {name}.");
                if (header.Schema?.Enum is { Count: > 0 } allowed)
                {
                    Assert.Contains(string.Join(", ", values), allowed.Select(value => value!.GetValue<string>()));
                }
            }

            if (documented.Content is null || documented.Content.Count == 0)
            {
                Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
                continue;
            }

            (string mediaType, IOpenApiMediaType content) = Assert.Single(documented.Content);
            IOpenApiSchema schema = content.Schema!;
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

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, ScenarioRequest scenarioRequest)
    {
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (scenarioRequest.Body is not null)
        {
            request.Content = new StringContent(scenarioRequest.Body, Encoding.UTF8, "application/json");
        }

        if (scenarioRequest.BearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenarioRequest.BearerToken);
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

    /// <summary>A scenario whose request is only a JSON body, built when the scenario runs (unique values).</summary>
    private static Func<HttpClient, IServiceProvider, Task<ScenarioRequest>> Json(Func<string> body) =>
        (_, _) => Task.FromResult(new ScenarioRequest(body()));

    /// <summary>Registers a new account, logs in, and returns its access token.</summary>
    private static async Task<string> AccessTokenAsync(HttpClient client)
    {
        (string email, _) = await SessionTests.RegisterAsync(client);
        return AccountSecurityTests.Access(await SessionTests.LoginJsonAsync(client, email));
    }

    /// <summary>Takes the only hashing slot (scenarios with <see cref="OneHashingSlot"/>) until disposed.</summary>
    private static async Task<IDisposable> TakeEveryHashingSlotAsync(IServiceProvider services) =>
        await services.GetRequiredService<PasswordHashingGate>().EnterAsync(TestContext.Current.CancellationToken);

    /// <param name="Prepare">Builds the request when the scenario runs; may first call the core
    /// (register, log in...) or use its services. Null: no body and no token.</param>
    /// <param name="Settings">Core settings for this scenario (e.g. low limits), or null.</param>
    /// <param name="RequestPath">Concrete path sent (path parameters filled in) when <paramref name="Path"/> is a template.</param>
    private sealed record ContractScenario(
        string Method, string Path, HttpStatusCode Status, bool DatabaseReachable,
        Func<HttpClient, IServiceProvider, Task<ScenarioRequest>>? Prepare = null,
        IReadOnlyDictionary<string, string>? Settings = null,
        string? RequestPath = null);

    /// <param name="Body">JSON request body, or null.</param>
    /// <param name="BearerToken">Access token sent in the Authorization header, or null.</param>
    /// <param name="Held">Disposed once the response is received (e.g. a hashing slot kept busy).</param>
    private sealed record ScenarioRequest(string? Body, string? BearerToken = null, IDisposable? Held = null);
}
