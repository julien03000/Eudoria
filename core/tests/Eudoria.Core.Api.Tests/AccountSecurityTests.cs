using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Eudoria.Core.Api.Tests;

/// <summary>POST /v1/me/password and POST /v1/me/sessions/revoke-all against the real core and database (plan 1.4).</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class AccountSecurityTests(PostgresFixture database)
{
    internal const string NewPassword = "Velours-Ardoise-Peuplier-71";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---------- Password change ----------

    [Fact]
    public async Task PasswordChange_Returns200_WithNewTokens_AndOnlyTheNewPasswordWorks()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        JsonNode tokens = await SessionTests.LoginJsonAsync(client, email);

        using HttpResponseMessage response = await ChangePasswordAsync(client, Access(tokens), SessionTests.Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        JsonNode fresh = await SessionTests.ReadJsonAsync(response);
        using HttpResponseMessage me = await SessionTests.GetMeAsync(client, Access(fresh));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using HttpResponseMessage oldPassword = await SessionTests.LoginAsync(client, email, SessionTests.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using HttpResponseMessage newPassword = await SessionTests.LoginAsync(client, email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task PasswordChange_CutsEveryOtherSession_AndEveryEarlierAccessToken()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        JsonNode thisDevice = await SessionTests.LoginJsonAsync(client, email);
        JsonNode otherDevice = await SessionTests.LoginJsonAsync(client, email);

        using HttpResponseMessage response = await ChangePasswordAsync(client, Access(thisDevice), SessionTests.Password, NewPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonNode fresh = await SessionTests.ReadJsonAsync(response);

        foreach (JsonNode old in new[] { thisDevice, otherDevice })
        {
            using HttpResponseMessage me = await SessionTests.GetMeAsync(client, Access(old));
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
            using HttpResponseMessage refresh = await SessionTests.RefreshAsync(client, Refresh(old));
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        using HttpResponseMessage freshRefresh = await SessionTests.RefreshAsync(client, Refresh(fresh));
        Assert.Equal(HttpStatusCode.OK, freshRefresh.StatusCode);
        JsonNode renewed = await SessionTests.ReadJsonAsync(freshRefresh);
        using HttpResponseMessage renewedMe = await SessionTests.GetMeAsync(client, Access(renewed));
        Assert.Equal(HttpStatusCode.OK, renewedMe.StatusCode);
    }

    [Fact]
    public async Task WrongCurrentPassword_Returns401_CountedLikeALogin_SixthIs429()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        string access = Access(await SessionTests.LoginJsonAsync(client, email));

        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage wrong = await ChangePasswordAsync(client, access, "not the right password", NewPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(
                """{"type":"about:blank","title":"Unauthorized","status":401,"errors":[{"field":"currentPassword","code":"invalid_credentials"}]}""",
                await wrong.Content.ReadAsStringAsync(Cancellation));
        }

        using HttpResponseMessage sixth = await ChangePasswordAsync(client, access, SessionTests.Password, NewPassword);
        await ThrottlingTests.AssertTooManyAsync(sixth, expectedRetryAfter: 60);

        using HttpResponseMessage me = await SessionTests.GetMeAsync(client, access); // nothing changed
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Theory]
    [InlineData("short", "password_too_short")]
    [InlineData("azertyuiop123", "password_too_common")]
    [InlineData("my-eudoria-passphrase", "password_too_common")]
    [InlineData(SessionTests.Password, "password_unchanged")]
    public async Task RefusedNewPassword_Returns400_AndChangesNothing(string newPassword, string code)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        string access = Access(await SessionTests.LoginJsonAsync(client, email));

        using HttpResponseMessage response = await ChangePasswordAsync(client, access, SessionTests.Password, newPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $$"""{"type":"about:blank","title":"Bad Request","status":400,"errors":[{"field":"newPassword","code":"{{code}}"}]}""",
            await response.Content.ReadAsStringAsync(Cancellation));
        using HttpResponseMessage me = await SessionTests.GetMeAsync(client, access);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task NewPasswordContainingThePseudo_IsRefused()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, string pseudo) = await SessionTests.RegisterAsync(client);
        string access = Access(await SessionTests.LoginJsonAsync(client, email));

        using HttpResponseMessage response = await ChangePasswordAsync(client, access, SessionTests.Password, $"{pseudo}-forever-2026");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("password_too_common", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"currentPassword":"a","newPassword":"b","extra":1}""")]
    [InlineData("""{"currentPassword":1,"newPassword":"b"}""")]
    public async Task UnreadableBody_Returns400_BodyInvalid(string body)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        string access = Access(await SessionTests.LoginJsonAsync(client, email));

        using HttpResponseMessage response = await SendAsync(client, "/v1/me/password", access, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("body_invalid", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCurrentPassword_Returns400_PasswordRequired()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        string access = Access(await SessionTests.LoginJsonAsync(client, email));

        using HttpResponseMessage response = await SendAsync(client, "/v1/me/password", access, """{"newPassword":"Velours-Ardoise-Peuplier-71"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"type":"about:blank","title":"Bad Request","status":400,"errors":[{"field":"currentPassword","code":"password_required"}]}""",
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task WithoutAccessToken_BothRoutesReturn401()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage password = await ChangePasswordAsync(client, accessToken: null, SessionTests.Password, NewPassword);
        using HttpResponseMessage revokeAll = await RevokeAllAsync(client, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, password.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revokeAll.StatusCode);
    }

    [Fact]
    public async Task Logs_NeverContainThePasswords()
    {
        CapturingLoggerProvider logs = new();
        await using CoreApiFactory factory = new(database.ConnectionString, logs);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        string access = Access(await SessionTests.LoginJsonAsync(client, email));

        using HttpResponseMessage wrong = await ChangePasswordAsync(client, access, "Wrong-Secret-Passphrase!", NewPassword);
        using HttpResponseMessage changed = await ChangePasswordAsync(client, access, SessionTests.Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Contains(logs.Entries, entry => entry.Contains("changed", StringComparison.Ordinal));
        foreach (string secret in new[] { "Wrong-Secret-Passphrase!", NewPassword, SessionTests.Password, email })
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------- Revoke all ----------

    [Fact]
    public async Task RevokeAll_Returns204_AndCutsEverySession_ThisOneIncluded_ThenLoginWorks()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        JsonNode thisDevice = await SessionTests.LoginJsonAsync(client, email);
        JsonNode otherDevice = await SessionTests.LoginJsonAsync(client, email);

        using HttpResponseMessage response = await RevokeAllAsync(client, Access(thisDevice));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Cancellation));
        foreach (JsonNode old in new[] { thisDevice, otherDevice })
        {
            using HttpResponseMessage me = await SessionTests.GetMeAsync(client, Access(old));
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
            using HttpResponseMessage refresh = await SessionTests.RefreshAsync(client, Refresh(old));
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        // Logging in again right away (same second) gives tokens that work.
        JsonNode again = await SessionTests.LoginJsonAsync(client, email);
        using HttpResponseMessage againMe = await SessionTests.GetMeAsync(client, Access(again));
        Assert.Equal(HttpStatusCode.OK, againMe.StatusCode);
    }

    [Fact]
    public async Task RevokeAll_DuringSimultaneousRenewals_LeavesNoRefreshTokenAlive()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await SessionTests.RegisterAsync(client);
        List<JsonNode> devices = [];
        for (int i = 0; i < 10; i++)
        {
            devices.Add(await SessionTests.LoginJsonAsync(client, email)); // one by one: at most 5 logins of one e-mail are checked at once
        }

        Guid accountId = Guid.Parse((await SessionTests.ReadJsonAsync(await SessionTests.GetMeAsync(client, Access(devices[0]))))["id"]!.GetValue<string>());

        Task<HttpResponseMessage>[] renewals = [.. devices.Select(device => SessionTests.RefreshAsync(client, Refresh(device)))];
        using HttpResponseMessage revokeAll = await RevokeAllAsync(client, Access(devices[0]));
        HttpResponseMessage[] renewed = await Task.WhenAll(renewals);

        Assert.Equal(HttpStatusCode.NoContent, revokeAll.StatusCode);
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        Assert.False(await identity.RefreshTokens.AnyAsync(token => token.AccountId == accountId && token.RevokedAt == null, Cancellation));
        foreach (HttpResponseMessage response in renewed.Where(response => response.StatusCode == HttpStatusCode.OK))
        {
            JsonNode pair = await SessionTests.ReadJsonAsync(response);
            using HttpResponseMessage me = await SessionTests.GetMeAsync(client, Access(pair));
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        Array.ForEach(renewed, response => response.Dispose());
    }

    internal static string Access(JsonNode tokens) => tokens["accessToken"]!.GetValue<string>();

    private static string Refresh(JsonNode tokens) => tokens["refreshToken"]!.GetValue<string>();

    internal static string ChangePasswordJson(string currentPassword, string newPassword) =>
        new JsonObject { ["currentPassword"] = currentPassword, ["newPassword"] = newPassword }.ToJsonString();

    internal static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string? accessToken, string currentPassword, string newPassword) =>
        SendAsync(client, "/v1/me/password", accessToken, ChangePasswordJson(currentPassword, newPassword));

    internal static Task<HttpResponseMessage> RevokeAllAsync(HttpClient client, string? accessToken) =>
        SendAsync(client, "/v1/me/sessions/revoke-all", accessToken, body: null);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string route, string? accessToken, string? body)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(route, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request, Cancellation);
    }
}
