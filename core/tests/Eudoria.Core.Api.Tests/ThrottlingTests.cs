using System.Net;
using System.Text;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Passwords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eudoria.Core.Api.Tests;

/// <summary>Limits on failed logins, request rates per client address and concurrent hashing (plan 1.4).</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ThrottlingTests(PostgresFixture database)
{
    private const string TooManyBody =
        """{"type":"about:blank","title":"Too Many Requests","status":429,"errors":[{"field":"request","code":"too_many_attempts"}]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SixthWrongPassword_Gets429_AndEvenTheRightPasswordIsRefused_WithoutBeingChecked()
    {
        CountingPasswordHasher hasher = new();
        await using CoreApiFactory factory = new(database.ConnectionString,
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IPasswordHasher>(hasher)));
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);

        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage wrong = await LoginAsync(client, email, "not the right password", "203.0.113.7");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        int verifiedBefore = hasher.VerifyCount;
        using HttpResponseMessage sixth = await LoginAsync(client, email, "not the right password", "203.0.113.7");
        using HttpResponseMessage right = await LoginAsync(client, email, SessionTests.Password, "203.0.113.7");

        await AssertTooManyAsync(sixth, expectedRetryAfter: 60);
        await AssertTooManyAsync(right, expectedRetryAfter: 60);
        Assert.Equal(verifiedBefore, hasher.VerifyCount);
    }

    [Fact]
    public async Task UnknownEmail_IsBlockedExactlyLikeAKnownOne()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string knownEmail, _) = await SessionTests.RegisterAsync(client);
        string unknownEmail = RegistrationTests.UniqueEmail();

        HttpResponseMessage[] blocked = new HttpResponseMessage[2];
        int index = 0;
        foreach (string email in new[] { knownEmail, unknownEmail })
        {
            for (int i = 0; i < 5; i++)
            {
                using HttpResponseMessage wrong = await LoginAsync(client, email, "not the right password", "203.0.113.7");
                Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            }

            blocked[index++] = await LoginAsync(client, email, "not the right password", "203.0.113.7");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked[0].StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked[1].StatusCode);
        Assert.Equal(blocked[0].Headers.RetryAfter?.ToString(), blocked[1].Headers.RetryAfter?.ToString());
        Assert.Equal(blocked[0].Content.Headers.ContentType?.ToString(), blocked[1].Content.Headers.ContentType?.ToString());
        Assert.Equal(await blocked[0].Content.ReadAsStringAsync(Cancellation), await blocked[1].Content.ReadAsStringAsync(Cancellation));
        Array.ForEach(blocked, response => response.Dispose());
    }

    [Fact]
    public async Task AnotherAddress_CanStillLogIn_UntilTheLimitForTheEmailAlone()
    {
        await using CoreApiFactory factory = new(database.ConnectionString,
            settings: new Dictionary<string, string> { ["Identity:Throttling:IdentifierMaxFailures"] = "8" });
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        await FailLoginsAsync(client, email, 5, "203.0.113.7");

        using HttpResponseMessage otherAddress = await LoginAsync(client, email, SessionTests.Password, "198.51.100.1");
        Assert.Equal(HttpStatusCode.OK, otherAddress.StatusCode);

        await FailLoginsAsync(client, email, 3, "198.51.100.2"); // 8 failures for this e-mail
        using HttpResponseMessage thirdAddress = await LoginAsync(client, email, SessionTests.Password, "192.0.2.50");
        await AssertTooManyAsync(thirdAddress, expectedRetryAfter: 15 * 60);
    }

    [Fact]
    public async Task FiftySimultaneousWrongPasswords_CheckAtMostFive_AndNeverFail()
    {
        CountingPasswordHasher hasher = new();
        await using CoreApiFactory factory = new(database.ConnectionString,
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IPasswordHasher>(hasher)));
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        int verifiedBefore = hasher.VerifyCount;

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => LoginAsync(client, email, "not the right password", "203.0.113.7")));

        Assert.InRange(hasher.VerifyCount - verifiedBefore, 1, 5);
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
        Assert.InRange(responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized), 1, 5);
        Array.ForEach(responses, response => response.Dispose());
    }

    [Fact]
    public async Task LoginRate_IsLimitedPerAddress_AndIpv6IsCountedPerSlash64()
    {
        await using CoreApiFactory factory = new(database.ConnectionString,
            settings: new Dictionary<string, string> { ["Identity:Throttling:Login:PermitLimit"] = "3" });
        using HttpClient client = factory.CreateClient();

        // Three different addresses of one /64 block: one client for the limit.
        foreach (string address in new[] { "2001:db8:1:2::1", "2001:db8:1:2::2", "2001:db8:1:2:aaaa:bbbb:cccc:dddd" })
        {
            using HttpResponseMessage allowed = await LoginAsync(client, RegistrationTests.UniqueEmail(), "whatever-password", address);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using HttpResponseMessage sameBlock = await LoginAsync(client, RegistrationTests.UniqueEmail(), "whatever-password", "2001:db8:1:2:ffff::9");
        using HttpResponseMessage otherBlock = await LoginAsync(client, RegistrationTests.UniqueEmail(), "whatever-password", "2001:db8:1:3::1");
        using HttpResponseMessage ipv4 = await LoginAsync(client, RegistrationTests.UniqueEmail(), "whatever-password", "203.0.113.7");

        await AssertTooManyAsync(sameBlock, expectedRetryAfter: null);
        Assert.Equal(HttpStatusCode.Unauthorized, otherBlock.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ipv4.StatusCode);
    }

    [Fact]
    public async Task RegistrationRate_IsLimitedPerAddress()
    {
        await using CoreApiFactory factory = new(database.ConnectionString,
            settings: new Dictionary<string, string> { ["Identity:Throttling:Registration:PermitLimit"] = "2" });
        using HttpClient client = factory.CreateClient();

        for (int i = 0; i < 2; i++)
        {
            using HttpResponseMessage allowed = await RegisterAsync(client, "203.0.113.7");
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using HttpResponseMessage third = await RegisterAsync(client, "203.0.113.7");
        using HttpResponseMessage otherAddress = await RegisterAsync(client, "203.0.113.8");

        await AssertTooManyAsync(third, expectedRetryAfter: null);
        Assert.Equal(HttpStatusCode.Accepted, otherAddress.StatusCode);
    }

    [Fact]
    public async Task RefreshRate_IsLimitedPerAddress()
    {
        await using CoreApiFactory factory = new(database.ConnectionString,
            settings: new Dictionary<string, string> { ["Identity:Throttling:Refresh:PermitLimit"] = "1" });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await PostAsync(client, "/v1/auth/refresh", SessionTests.RefreshTokenJson(UnknownRefreshToken), "203.0.113.7");
        using HttpResponseMessage second = await PostAsync(client, "/v1/auth/refresh", SessionTests.RefreshTokenJson(UnknownRefreshToken), "203.0.113.7");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        await AssertTooManyAsync(second, expectedRetryAfter: null);
    }

    [Fact]
    public async Task NoHashingSlotInTime_Gets503_ServerBusy_WithRetryAfter()
    {
        await using CoreApiFactory factory = new(database.ConnectionString, settings: new Dictionary<string, string>
        {
            ["Identity:Throttling:MaxConcurrentHashes"] = "1",
            ["Identity:Throttling:HashQueueTimeout"] = "00:00:00.200",
        });
        using HttpClient client = factory.CreateClient();
        PasswordHashingGate gate = factory.Services.GetRequiredService<PasswordHashingGate>();

        HttpResponseMessage login;
        HttpResponseMessage registration;
        using (await gate.EnterAsync(Cancellation)) // every slot taken
        {
            login = await LoginAsync(client, RegistrationTests.UniqueEmail(), "whatever-password", "203.0.113.7");
            registration = await RegisterAsync(client, "203.0.113.7");
        }

        foreach (HttpResponseMessage response in new[] { login, registration })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("1", response.Headers.RetryAfter?.ToString());
            Assert.Equal(
                """{"type":"about:blank","title":"Service Unavailable","status":503,"errors":[{"field":"request","code":"server_busy"}]}""",
                await response.Content.ReadAsStringAsync(Cancellation));
            response.Dispose();
        }

        using HttpResponseMessage afterwards = await RegisterAsync(client, "203.0.113.7");
        Assert.Equal(HttpStatusCode.Accepted, afterwards.StatusCode);
    }

    [Fact]
    public async Task Logs_NeverContainTheEmailOrTheAddress()
    {
        CapturingLoggerProvider logs = new();
        await using CoreApiFactory factory = new(database.ConnectionString, logs);
        using HttpClient client = factory.CreateClient();
        string email = RegistrationTests.UniqueEmail();

        await FailLoginsAsync(client, email, 5, "203.0.113.77");
        using HttpResponseMessage blocked = await LoginAsync(client, email, "not the right password", "203.0.113.77");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains(logs.Entries, entry => entry.Contains("too many failed attempts", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("203.0.113.77", StringComparison.Ordinal));
    }

    // A well-formed refresh token that was never issued.
    private const string UnknownRefreshToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    internal static async Task AssertTooManyAsync(HttpResponseMessage response, int? expectedRetryAfter)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TooManyBody, await response.Content.ReadAsStringAsync(Cancellation));
        string? retryAfter = response.Headers.RetryAfter?.ToString();
        Assert.Matches("^[1-9][0-9]*$", retryAfter);
        if (expectedRetryAfter is not null)
        {
            Assert.Equal(expectedRetryAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), retryAfter);
        }
    }

    internal static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string clientAddress) =>
        PostAsync(client, "/v1/auth/login", SessionTests.LoginJson(email, password), clientAddress);

    private static async Task FailLoginsAsync(HttpClient client, string email, int count, string clientAddress)
    {
        for (int i = 0; i < count; i++)
        {
            using HttpResponseMessage wrong = await LoginAsync(client, email, "not the right password", clientAddress);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string clientAddress) =>
        PostAsync(client, "/v1/accounts",
            RegistrationTests.RegistrationJson(RegistrationTests.UniqueEmail(), RegistrationTests.UniquePseudo(), SessionTests.Password),
            clientAddress);

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, string body, string clientAddress)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(route, UriKind.Relative));
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add(CoreApiFactory.ClientAddressHeader, clientAddress);
        return await client.SendAsync(request, Cancellation);
    }
}
