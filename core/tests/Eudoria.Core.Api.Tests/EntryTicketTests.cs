using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Tokens;
using Eudoria.ThirdParty.WorldVerifier;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Eudoria.Core.Api.Tests;

/// <summary>
/// World entry tickets (plan 1.6, ADR-0007): issuing, publishing the keys, separation from the access
/// token, and the ROADMAP criterion — an independent third party verifies a ticket and gets the account.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class EntryTicketTests(PostgresFixture database)
{
    private const string World = "sandbox";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---------- Issuing ----------

    [Fact]
    public async Task Ticket_HasExactlyTheExpectedHeaderAndClaims_LivesSixtySeconds_AndIsNotCacheable()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string accessToken, Guid accountId) = await LogInAsync(client);

        using HttpResponseMessage response = await RequestTicketAsync(client, accessToken, World);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        JsonNode body = await SessionTests.ReadJsonAsync(response);
        Assert.Equal(60, body["expiresIn"]!.GetValue<long>());
        string ticket = body["ticket"]!.GetValue<string>();

        JsonObject header = DecodePart(ticket, 0);
        Assert.Equal(["alg", "kid", "typ"], header.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("ES256", header["alg"]!.GetValue<string>());
        Assert.Equal("eudoria-entry+jwt", header["typ"]!.GetValue<string>());
        Assert.Equal(TestSigningKey.EntryTicketKey.SecurityKey.KeyId, header["kid"]!.GetValue<string>());

        JsonObject claims = DecodePart(ticket, 1);
        Assert.Equal(["aud", "exp", "iat", "iss", "jti", "sub"], claims.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("eudoria-core", claims["iss"]!.GetValue<string>());
        Assert.Equal(accountId.ToString(), claims["sub"]!.GetValue<string>());
        Assert.Equal("eudoria-world:sandbox", claims["aud"]!.GetValue<string>()); // one string, not a list
        Assert.Equal(60, claims["exp"]!.GetValue<long>() - claims["iat"]!.GetValue<long>());
        Assert.True(Guid.TryParse(claims["jti"]!.GetValue<string>(), out Guid jti));
        Assert.Equal(4, jti.Version);
    }

    [Fact]
    public async Task EveryTicket_HasItsOwnJti()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string accessToken, _) = await LogInAsync(client);

        string first = await TicketAsync(client, accessToken, World);
        string second = await TicketAsync(client, accessToken, World);

        Assert.NotEqual(DecodePart(first, 1)["jti"]!.GetValue<string>(), DecodePart(second, 1)["jti"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    [InlineData("revoked")]
    [InlineData("entry-ticket")]
    public async Task Ticket_WithoutAValidAccessToken_Returns401(string kind)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string accessToken, _) = await LogInAsync(client);
        string? token = kind switch
        {
            "missing" => null,
            "garbage" => "not-a-jwt",
            "revoked" => await RevokeAllAndKeepAsync(client, accessToken),
            "entry-ticket" => await TicketAsync(client, accessToken, World),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        using HttpResponseMessage response = await RequestTicketAsync(client, token, World);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("Sandbox")] // upper case
    [InlineData("-sandbox")]
    [InlineData("sandbox-")]
    [InlineData("sand_box")]
    [InlineData("a%2Fb")] // an encoded slash stays in the value
    [InlineData("a.b")] // ("../" never reaches the route: HTTP clients remove dot segments before sending)
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 characters
    public async Task MalformedWorldId_Returns400(string worldId)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string accessToken, _) = await LogInAsync(client);

        using HttpResponseMessage response = await RequestTicketAsync(client, accessToken, worldId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"type":"about:blank","title":"Bad Request","status":400,"errors":[{"field":"worldId","code":"world_id_invalid"}]}""",
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task UnknownWorld_Returns404_ButAWellFormed64CharacterIdIsAccepted()
    {
        string longWorld = new('a', 64);
        await using CoreApiFactory factory = new(database.ConnectionString,
            settings: new Dictionary<string, string> { ["Identity:EntryTickets:KnownWorlds:1"] = longWorld });
        using HttpClient client = factory.CreateClient();
        (string accessToken, _) = await LogInAsync(client);

        using HttpResponseMessage unknown = await RequestTicketAsync(client, accessToken, "no-such-world");
        using HttpResponseMessage known = await RequestTicketAsync(client, accessToken, longWorld);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(
            """{"type":"about:blank","title":"Not Found","status":404,"errors":[{"field":"worldId","code":"world_unknown"}]}""",
            await unknown.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
    }

    [Fact]
    public async Task Tickets_AreRateLimitedPerClientAddress()
    {
        await using CoreApiFactory factory = new(database.ConnectionString,
            settings: new Dictionary<string, string> { ["Identity:Throttling:EntryTicket:PermitLimit"] = "2" });
        using HttpClient client = factory.CreateClient();
        (string accessToken, _) = await LogInAsync(client);

        using HttpResponseMessage first = await RequestTicketAsync(client, accessToken, World);
        using HttpResponseMessage second = await RequestTicketAsync(client, accessToken, World);
        using HttpResponseMessage third = await RequestTicketAsync(client, accessToken, World);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
    }

    // ---------- Separation from the access token (plan 1.6, section 5.4) ----------

    [Fact]
    public async Task Ticket_PresentedToTheCoreAsABearerToken_IsRefused()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string accessToken, _) = await LogInAsync(client);
        string ticket = await TicketAsync(client, accessToken, World);

        using HttpResponseMessage me = await SessionTests.GetMeAsync(client, ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task AccessTokenSignedWithTheTicketKey_IsRefusedByTheCore()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (_, Guid accountId) = await LogInAsync(client);
        // Right type, issuer and audience: only the key differs.
        AccessTokenIssuer forger = new(TestSigningKey.EntryTicketKey, Microsoft.Extensions.Options.Options.Create(new TokenOptions
        {
            Issuer = "eudoria-core",
            Audience = "eudoria-core",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));

        using HttpResponseMessage me = await SessionTests.GetMeAsync(client, forger.Issue(accountId, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // ---------- Public keys ----------

    [Fact]
    public async Task Keys_ArePublic_PublicPartOnly_WithAnRfc7638Kid_AndCacheableFiveMinutes()
    {
        // No database needed: the route is static.
        await using CoreApiFactory factory = new(CoreApiFactory.UnreachableDatabase);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await GetKeysAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/jwk-set+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
        JsonNode body = await SessionTests.ReadJsonAsync(response);
        JsonObject key = Assert.IsType<JsonObject>(Assert.Single(body["keys"]!.AsArray()));
        Assert.Equal(["alg", "crv", "kid", "kty", "use", "x", "y"], key.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.False(key.ContainsKey("d"));
        Assert.Equal("EC", key["kty"]!.GetValue<string>());
        Assert.Equal("P-256", key["crv"]!.GetValue<string>());
        Assert.Equal("ES256", key["alg"]!.GetValue<string>());
        Assert.Equal("sig", key["use"]!.GetValue<string>());

        // The ticket key, and its kid recomputed by hand from the published coordinates (RFC 7638, section 3).
        ECParameters expected = TestSigningKey.EntryTicketKey.SecurityKey.ECDsa.ExportParameters(includePrivateParameters: false);
        string x = key["x"]!.GetValue<string>();
        string y = key["y"]!.GetValue<string>();
        Assert.Equal(Base64Url.EncodeToString(expected.Q.X), x);
        Assert.Equal(Base64Url.EncodeToString(expected.Q.Y), y);
        string canonical = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" + x + "\",\"y\":\"" + y + "\"}";
        Assert.Equal(Base64Url.EncodeToString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))), key["kid"]!.GetValue<string>());

        // The access-token key is never published.
        ECParameters accessKey = TestSigningKey.Key.SecurityKey.ECDsa.ExportParameters(includePrivateParameters: false);
        string published = body.ToJsonString();
        Assert.DoesNotContain(Base64Url.EncodeToString(accessKey.Q.X), published, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSigningKey.Key.SecurityKey.KeyId, published, StringComparison.Ordinal);
    }

    // ---------- The ROADMAP criterion: a third party verifies a ticket ----------

    [Fact]
    public async Task ThirdParty_VerifiesATicket_WithoutTheCore_AndGetsTheSameAccountAsMe()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string accessToken, Guid accountId) = await LogInAsync(client);
        using HttpResponseMessage me = await SessionTests.GetMeAsync(client, accessToken);
        Guid meId = Guid.Parse((await SessionTests.ReadJsonAsync(me))["id"]!.GetValue<string>());
        using EntryTicketKeyStore keys = new(factory.CreateClient(), new Uri("/v1/entry-tickets/keys", UriKind.Relative), TimeProvider.System);
        Assert.True(await keys.RefreshAsync(Cancellation)); // at start-up, like a world server
        WorldTicketVerifier sandbox = new(World, keys, new ReplayCache(), TimeProvider.System);
        WorldTicketVerifier otherWorld = new("other-world", keys, new ReplayCache(), TimeProvider.System);

        string ticket = await TicketAsync(client, accessToken, World);
        int downloads = keys.Downloads;
        TicketVerdict verdict = await sandbox.VerifyAsync(ticket, Cancellation);
        TicketVerdict replay = await sandbox.VerifyAsync(ticket, Cancellation);
        TicketVerdict elsewhere = await otherWorld.VerifyAsync(await TicketAsync(client, accessToken, World), Cancellation);
        TicketVerdict accessTokenAsTicket = await sandbox.VerifyAsync(accessToken, Cancellation);

        Assert.True(verdict.IsAccepted, verdict.ToString());
        Assert.Equal(accountId, verdict.AccountId);
        Assert.Equal(meId, verdict.AccountId);
        Assert.Equal(downloads, keys.Downloads); // verified offline, with the keys already in memory
        Assert.Equal(TicketRejections.Replayed, replay.Rejection);
        Assert.Equal(TicketRejections.AudienceInvalid, elsewhere.Rejection);
        Assert.Equal(TicketRejections.TypeNotAllowed, accessTokenAsTicket.Rejection);
    }

    [Fact]
    public async Task ThirdParty_RefusesATicketOfACoreWithAnotherKey()
    {
        await using CoreApiFactory realCore = new(database.ConnectionString);
        await using CoreApiFactory otherCore = new(database.ConnectionString, entryTicketSigningKey: TestSigningKey.NewBase64Key(ECCurve.NamedCurves.nistP256));
        using HttpClient client = otherCore.CreateClient();
        (string accessToken, _) = await LogInAsync(client);
        using EntryTicketKeyStore keys = new(realCore.CreateClient(), new Uri("/v1/entry-tickets/keys", UriKind.Relative), TimeProvider.System);
        await keys.RefreshAsync(Cancellation);

        TicketVerdict verdict = await new WorldTicketVerifier(World, keys, new ReplayCache(), TimeProvider.System)
            .VerifyAsync(await TicketAsync(client, accessToken, World), Cancellation);

        Assert.Equal(TicketRejections.KeyUnknown, verdict.Rejection);
    }

    // ---------- Start-up and logs ----------

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("same-as-access-key")]
    public async Task Core_RefusesToStart_WithoutAValidDistinctTicketKey(string kind)
    {
        string key = kind switch
        {
            "missing" => string.Empty,
            "invalid" => "AAAA",
            _ => TestSigningKey.Base64,
        };
        await using CoreApiFactory factory = new(database.ConnectionString, entryTicketSigningKey: key);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        string message = exception.ToString();
        Assert.Contains(kind == "same-as-access-key" ? "must differ" : "entry ticket signing key", message, StringComparison.Ordinal);
        Assert.Contains("new-signing-key.ps1 -EntryTickets", message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSigningKey.Base64, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Identity:EntryTickets:KnownWorlds:0", "Not_Valid")]
    [InlineData("Identity:EntryTickets:Lifetime", "00:10:00")]
    [InlineData("Identity:EntryTickets:Lifetime", "00:00:00")]
    public async Task Core_RefusesToStart_WithInvalidTicketSettings(string setting, string value)
    {
        await using CoreApiFactory factory = new(database.ConnectionString, settings: new Dictionary<string, string> { [setting] = value });

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Identity:EntryTickets", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_SayATicketWasIssued_ButNeverContainItOrTheAccessToken()
    {
        CapturingLoggerProvider logs = new();
        await using CoreApiFactory factory = new(database.ConnectionString, logs);
        using HttpClient client = factory.CreateClient();
        (string accessToken, Guid accountId) = await LogInAsync(client);

        string ticket = await TicketAsync(client, accessToken, World);
        using HttpResponseMessage refused = await SessionTests.GetMeAsync(client, ticket);

        Assert.Contains(logs.Entries, entry => entry.Contains($"Entry ticket issued for account {accountId} to world sandbox", StringComparison.Ordinal));
        foreach (string secret in new[] { ticket, ticket.Split('.')[1], ticket.Split('.')[2], accessToken, accessToken.Split('.')[2] })
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.Contains(secret, StringComparison.Ordinal));
        }
    }

    // ---------- Helpers ----------

    internal static async Task<HttpResponseMessage> RequestTicketAsync(HttpClient client, string? accessToken, string worldId)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"/v1/worlds/{worldId}/entry-tickets", UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request, Cancellation);
    }

    internal static async Task<string> TicketAsync(HttpClient client, string accessToken, string worldId)
    {
        using HttpResponseMessage response = await RequestTicketAsync(client, accessToken, worldId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await SessionTests.ReadJsonAsync(response))["ticket"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> GetKeysAsync(HttpClient client) =>
        client.GetAsync(new Uri("/v1/entry-tickets/keys", UriKind.Relative), Cancellation);

    /// <summary>Registers a new account and logs in: its access token and identifier.</summary>
    private static async Task<(string AccessToken, Guid AccountId)> LogInAsync(HttpClient client)
    {
        (string email, _) = await SessionTests.RegisterAsync(client);
        string accessToken = AccountSecurityTests.Access(await SessionTests.LoginJsonAsync(client, email));
        return (accessToken, Guid.Parse(new JsonWebToken(accessToken).Subject));
    }

    /// <summary>"Revoke all sessions" with this token, then returns it (now revoked).</summary>
    private static async Task<string> RevokeAllAndKeepAsync(HttpClient client, string accessToken)
    {
        using HttpResponseMessage revoked = await AccountSecurityTests.RevokeAllAsync(client, accessToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        return accessToken;
    }

    private static JsonObject DecodePart(string token, int index) =>
        JsonNode.Parse(Base64Url.DecodeFromChars(token.Split('.')[index]))!.AsObject();
}
