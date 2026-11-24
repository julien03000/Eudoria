using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Eudoria.Core.Api.Tests;

/// <summary>Login, renewal, logout and GET /v1/me against the real core and a real PostgreSQL database.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class SessionTests(PostgresFixture database)
{
    internal const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---------- Login ----------

    [Fact]
    public async Task ValidLogin_Returns200_WithTwoTokens_NotCacheable_AndStoresOnlyTheRefreshTokenHash()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);

        using HttpResponseMessage response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        JsonNode body = await ReadJsonAsync(response);
        Assert.Equal("Bearer", body["tokenType"]!.GetValue<string>());
        Assert.Equal(900, body["expiresIn"]!.GetValue<long>());
        Assert.Equal(7 * 24 * 3600, body["refreshExpiresIn"]!.GetValue<long>());
        string refreshToken = body["refreshToken"]!.GetValue<string>();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", refreshToken);

        byte[] expectedHash = SHA256.HashData(Base64Url.DecodeFromChars(refreshToken));
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        RefreshToken stored = await identity.RefreshTokens.AsNoTracking().SingleAsync(token => token.TokenHash == expectedHash, Cancellation);
        Assert.Null(stored.RevokedAt);
        Assert.Equal(stored.CreatedAt.AddDays(30), stored.SessionExpiresAt);
        Assert.False(await identity.RefreshTokens.AnyAsync(
            token => token.TokenHash == Encoding.ASCII.GetBytes(refreshToken), Cancellation));
    }

    [Fact]
    public async Task Login_NormalizesTheEmail_AndThePasswordToNfkc_LikeRegistration()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client, password: "café-au-lait-123");

        // Upper case and spaces around the e-mail; decomposed accent and full-width digits in the password.
        using HttpResponseMessage response = await LoginAsync(client, $"  {email.ToUpperInvariant()} ", "café-au-lait-１２３");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEmail_AndWrongPassword_GetTheSameAnswer_AndBothVerifyAPassword()
    {
        CountingPasswordHasher hasher = new();
        await using CoreApiFactory factory = new(database.ConnectionString,
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IPasswordHasher>(hasher)));
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);

        int before = hasher.VerifyCount;
        using HttpResponseMessage wrongPassword = await LoginAsync(client, email, "not the right password");
        using HttpResponseMessage unknownEmail = await LoginAsync(client, RegistrationTests.UniqueEmail(), Password);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        Assert.Equal(wrongPassword.Content.Headers.ContentType?.ToString(), unknownEmail.Content.Headers.ContentType?.ToString());
        string expected = """{"type":"about:blank","title":"Unauthorized","status":401,"errors":[{"field":"credentials","code":"invalid_credentials"}]}""";
        Assert.Equal(expected, await wrongPassword.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(expected, await unknownEmail.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(before + 2, hasher.VerifyCount);
    }

    [Fact]
    public async Task Login_WithAHashAtTheOldCost_Succeeds_AndUpgradesTheHash()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        // Cost of the accounts created during sub-step 1.2 (19 MiB, 2 passes).
        string oldHash = new Argon2idPasswordHasher(new Argon2idOptions(MemoryKib: 19456, Passes: 2)).Hash(Password);
        await using (IdentityDbContext identity = database.CreateIdentityDbContext())
        {
            await identity.Accounts.Where(account => account.Email == email)
                .ExecuteUpdateAsync(setters => setters.SetProperty(account => account.PasswordHash, oldHash), Cancellation);
        }

        using HttpResponseMessage response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Account account = await FindByEmailAsync(email);
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", account.PasswordHash, StringComparison.Ordinal);
        Assert.True(new Argon2idPasswordHasher(Argon2idOptions.Default).Verify(Password, account.PasswordHash));
    }

    [Fact]
    public async Task Login_WithAHashAtTheCurrentCost_DoesNotRewriteIt()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string before = (await FindByEmailAsync(email)).PasswordHash;

        using HttpResponseMessage response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, (await FindByEmailAsync(email)).PasswordHash);
    }

    // ---------- GET /v1/me and access-token validation ----------

    [Fact]
    public async Task Me_WithAValidAccessToken_ReturnsTheProfile_WithoutTheEmail()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, string pseudo) = await RegisterAsync(client);
        JsonNode tokens = await LoginJsonAsync(client, email);
        string accessToken = tokens["accessToken"]!.GetValue<string>();

        using HttpResponseMessage response = await GetMeAsync(client, accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Account account = await FindByEmailAsync(email);
        JsonNode body = await ReadJsonAsync(response);
        Assert.Equal(account.Id, body["id"]!.GetValue<Guid>());
        Assert.Equal(pseudo, body["pseudo"]!.GetValue<string>());
        Assert.Equal(account.CreatedAt, body["createdAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(3, body.AsObject().Count);
        Assert.Equal(account.Id.ToString(), new JsonWebToken(accessToken).Subject);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    [InlineData("expired")]
    [InlineData("no-expiration")]
    [InlineData("tampered-payload")]
    [InlineData("alg-none")]
    [InlineData("hs256-random-secret")]
    [InlineData("hs256-public-key-as-secret")]
    [InlineData("signed-by-another-key")]
    [InlineData("audience-eudoria")]
    [InlineData("audience-of-a-world")]
    [InlineData("other-issuer")]
    public async Task Me_RejectsMissingExpiredForgedOrForeignTokens_With401(string kind)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string validToken = (await LoginJsonAsync(client, email))["accessToken"]!.GetValue<string>();
        string subject = new JsonWebToken(validToken).Subject;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string? token = kind switch
        {
            "missing" => null,
            "garbage" => "not-a-jwt",
            "expired" => Forge(subject, issuedAt: now.AddHours(-1), expires: now.AddMinutes(-5)),
            "no-expiration" => Forge(subject, hasExpiration: false),
            "tampered-payload" => TamperPayload(validToken),
            "alg-none" => Unsigned(validToken),
            "hs256-random-secret" => Forge(subject, credentials: new SigningCredentials(
                new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256)),
            "hs256-public-key-as-secret" => Forge(subject, credentials: new SigningCredentials(
                new SymmetricSecurityKey(TestSigningKey.Key.SecurityKey.ECDsa.ExportSubjectPublicKeyInfo()) { KeyId = TestSigningKey.Key.SecurityKey.KeyId },
                SecurityAlgorithms.HmacSha256)),
            "signed-by-another-key" => ForgeWithAnotherKey(subject),
            "audience-eudoria" => Forge(subject, audience: "eudoria"),
            "audience-of-a-world" => Forge(subject, audience: "world-xyz"),
            "other-issuer" => Forge(subject, issuer: "someone-else"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        using HttpResponseMessage response = await GetMeAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Me_AcceptsATokenForgedWithTheRealKey_SoTheRejectionsAboveAreMeaningful()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string subject = (await FindByEmailAsync(email)).Id.ToString();

        using HttpResponseMessage response = await GetMeAsync(client, Forge(subject));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AccessToken_HasOnlyTheExpectedHeaderAndClaims()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);

        JsonWebToken token = new((await LoginJsonAsync(client, email))["accessToken"]!.GetValue<string>());

        Assert.Equal("ES256", token.Alg);
        Assert.Equal("JWT", token.Typ);
        Assert.Equal(TestSigningKey.Key.SecurityKey.KeyId, token.Kid);
        Assert.Equal(["aud", "exp", "iat", "iss", "jti", "sub"], token.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal));
        Assert.Equal("eudoria-core", Assert.Single(token.Audiences));
        Assert.Equal("eudoria-core", token.Issuer);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.IssuedAt);
        Assert.DoesNotContain(email, token.EncodedPayload, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Renewal ----------

    [Fact]
    public async Task Refresh_ReturnsANewPair_AndTheNewRefreshTokenWorksInTurn()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        JsonNode first = await LoginJsonAsync(client, email);

        using HttpResponseMessage second = await RefreshAsync(client, first["refreshToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.Headers.CacheControl?.NoStore);
        JsonNode secondBody = await ReadJsonAsync(second);
        Assert.NotEqual(first["refreshToken"]!.GetValue<string>(), secondBody["refreshToken"]!.GetValue<string>());
        Assert.NotEqual(first["accessToken"]!.GetValue<string>(), secondBody["accessToken"]!.GetValue<string>());

        using HttpResponseMessage third = await RefreshAsync(client, secondBody["refreshToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        using HttpResponseMessage me = await GetMeAsync(client, (await ReadJsonAsync(third))["accessToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task ReusingAConsumedRefreshToken_RevokesTheWholeSession()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string stolen = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();
        using HttpResponseMessage legitimate = await RefreshAsync(client, stolen);
        string latest = (await ReadJsonAsync(legitimate))["refreshToken"]!.GetValue<string>();

        using HttpResponseMessage reuse = await RefreshAsync(client, stolen);
        using HttpResponseMessage afterTheft = await RefreshAsync(client, latest);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(
            """{"type":"about:blank","title":"Unauthorized","status":401,"errors":[{"field":"refreshToken","code":"refresh_token_invalid"}]}""",
            await reuse.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheft.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // well-formed, but never issued
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // not Base64 URL-safe
    public async Task Refresh_WithAnUnknownOrMalformedToken_Returns401(string refreshToken)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RefreshAsync(client, refreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredRefreshToken_Returns401_WithoutRevokingTheSession()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string refreshToken = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();
        await UpdateStoredTokenAsync(refreshToken, setters => setters.SetProperty(token => token.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));

        using HttpResponseMessage response = await RefreshAsync(client, refreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null((await FindStoredTokenAsync(refreshToken)).RevokedAt);
    }

    [Fact]
    public async Task Session_NeverOutlivesItsMaximumDuration()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string refreshToken = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();

        // Session ending in one hour: the renewed token must not live longer than that.
        await UpdateStoredTokenAsync(refreshToken, setters => setters.SetProperty(token => token.SessionExpiresAt, DateTimeOffset.UtcNow.AddHours(1)));
        using HttpResponseMessage renewed = await RefreshAsync(client, refreshToken);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        JsonNode body = await ReadJsonAsync(renewed);
        Assert.InRange(body["refreshExpiresIn"]!.GetValue<long>(), 3500, 3600);

        // Session over (30 days after login): refused.
        string next = body["refreshToken"]!.GetValue<string>();
        await UpdateStoredTokenAsync(next, setters => setters.SetProperty(token => token.SessionExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        using HttpResponseMessage expired = await RefreshAsync(client, next);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task SimultaneousRenewals_WithTheSameToken_SucceedAtMostOnce_AndNeverFail500()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string refreshToken = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => RefreshAsync(client, refreshToken)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        // Strict version: the race is treated as a reuse, so even the winner's new token is revoked.
        string winnerToken = (await ReadJsonAsync(responses.Single(response => response.IsSuccessStatusCode)))["refreshToken"]!.GetValue<string>();
        Assert.NotNull((await FindStoredTokenAsync(winnerToken)).RevokedAt);
        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    // ---------- Logout ----------

    [Fact]
    public async Task Logout_Returns204_ThenTheRefreshTokenNoLongerWorks_ButOtherSessionsDo()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string thisDevice = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();
        string otherDevice = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();

        using HttpResponseMessage logout = await LogoutAsync(client, thisDevice);
        using HttpResponseMessage refreshAfterLogout = await RefreshAsync(client, thisDevice);
        using HttpResponseMessage otherSession = await RefreshAsync(client, otherDevice);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Empty(await logout.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherSession.StatusCode);
    }

    [Fact]
    public async Task Logout_AfterRenewals_ClosesTheSessionWhateverTheTokenUsed()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string first = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();
        using HttpResponseMessage renewed = await RefreshAsync(client, first);
        string latest = (await ReadJsonAsync(renewed))["refreshToken"]!.GetValue<string>();

        using HttpResponseMessage logout = await LogoutAsync(client, latest);
        using HttpResponseMessage refresh = await RefreshAsync(client, latest);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task TokenOfALoggedOutSession_IsRefused_WithoutATheftWarning()
    {
        CapturingLoggerProvider logs = new();
        await using CoreApiFactory factory = new(database.ConnectionString, logs);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client);
        string refreshToken = (await LoginJsonAsync(client, email))["refreshToken"]!.GetValue<string>();
        using HttpResponseMessage logout = await LogoutAsync(client, refreshToken);

        using HttpResponseMessage refresh = await RefreshAsync(client, refreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Contains(logs.Entries, entry => entry.Contains("closed session", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("theft", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("not-a-token")]
    [InlineData("")]
    public async Task Logout_WithAnUnknownToken_StillReturns204(string refreshToken)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await LogoutAsync(client, refreshToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---------- Robustness, logs, configuration ----------

    [Theory]
    [InlineData("/v1/auth/login", "not json")]
    [InlineData("/v1/auth/login", "[]")]
    [InlineData("/v1/auth/login", """{"email":"a@b.cd","password":"x","pseudo":"Ana_42"}""")]
    [InlineData("/v1/auth/login", """{"email":"a@b.cd","password":42}""")]
    [InlineData("/v1/auth/login", """{"email":"a@b.cd","email":"b@b.cd","password":"x"}""")]
    [InlineData("/v1/auth/refresh", "not json")]
    [InlineData("/v1/auth/refresh", """{"refresh_token":"x"}""")]
    [InlineData("/v1/auth/refresh", """{"refreshToken":["x"]}""")]
    [InlineData("/v1/auth/logout", "not json")]
    [InlineData("/v1/auth/logout", """{"RefreshToken":"x"}""")]
    public async Task UnreadableBody_Returns400_BodyInvalid(string route, string body)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await PostJsonAsync(client, route, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"type":"about:blank","title":"Bad Request","status":400,"errors":[{"field":"body","code":"body_invalid"}]}""",
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task Logs_NeverContainThePassword_TheEmail_OrAnyToken()
    {
        const string secretPassword = "Sup3r-Secret-Login-Passphrase!";
        CapturingLoggerProvider logs = new();
        await using CoreApiFactory factory = new(database.ConnectionString, logs);
        using HttpClient client = factory.CreateClient();
        (string email, _) = await RegisterAsync(client, password: secretPassword);

        using HttpResponseMessage failed = await LoginAsync(client, email, secretPassword + "x");
        JsonNode tokens = await LoginJsonAsync(client, email, secretPassword);
        string refreshToken = tokens["refreshToken"]!.GetValue<string>();
        string accessToken = tokens["accessToken"]!.GetValue<string>();
        using HttpResponseMessage me = await GetMeAsync(client, accessToken);
        using HttpResponseMessage renewed = await RefreshAsync(client, refreshToken);
        using HttpResponseMessage reuse = await RefreshAsync(client, refreshToken);
        using HttpResponseMessage logout = await LogoutAsync(client, refreshToken);

        Assert.Contains(logs.Entries, entry => entry.Contains("logged in", StringComparison.Ordinal)); // logging is on
        Assert.Contains(logs.Entries, entry => entry.Contains("Possible token theft", StringComparison.Ordinal));
        string[] secrets = [secretPassword, email, refreshToken, accessToken, accessToken.Split('.')[2]];
        foreach (string secret in secrets)
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !")]
    [InlineData("AAAA")]
    [InlineData("p384")]
    public async Task Core_RefusesToStart_WithoutAValidSigningKey(string signingKey)
    {
        string key = signingKey == "p384" ? TestSigningKey.NewBase64Key(ECCurve.NamedCurves.nistP384) : signingKey;
        await using CoreApiFactory factory = new(database.ConnectionString, signingKey: key);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        string message = exception.ToString();
        Assert.Contains("signing key", message, StringComparison.Ordinal);
        Assert.Contains("new-signing-key.ps1", message, StringComparison.Ordinal);
        if (key.Length > 8)
        {
            Assert.DoesNotContain(key, message, StringComparison.Ordinal);
        }
    }

    // ---------- Helpers ----------

    internal static async Task<(string Email, string Pseudo)> RegisterAsync(HttpClient client, string password = Password)
    {
        string email = RegistrationTests.UniqueEmail();
        string pseudo = RegistrationTests.UniquePseudo();
        using HttpResponseMessage response = await PostJsonAsync(client, "/v1/accounts", RegistrationTests.RegistrationJson(email, pseudo, password));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (email, pseudo);
    }

    internal static string LoginJson(string email, string password) =>
        new JsonObject { ["email"] = email, ["password"] = password }.ToJsonString();

    internal static string RefreshTokenJson(string refreshToken) =>
        new JsonObject { ["refreshToken"] = refreshToken }.ToJsonString();

    internal static async Task<JsonNode> LoginJsonAsync(HttpClient client, string email, string password = Password)
    {
        using HttpResponseMessage response = await LoginAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        PostJsonAsync(client, "/v1/auth/login", LoginJson(email, password));

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        PostJsonAsync(client, "/v1/auth/refresh", RefreshTokenJson(refreshToken));

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string refreshToken) =>
        PostJsonAsync(client, "/v1/auth/logout", RefreshTokenJson(refreshToken));

    private static async Task<HttpResponseMessage> GetMeAsync(HttpClient client, string? accessToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/v1/me", UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request, Cancellation);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string route, string body)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri(route, UriKind.Relative), content, Cancellation);
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancellation))!;

    /// <summary>A token like the core's, signed with the core's key unless other credentials are given.</summary>
    private static string Forge(
        string subject,
        string issuer = "eudoria-core",
        string audience = "eudoria-core",
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expires = null,
        SigningCredentials? credentials = null,
        bool hasExpiration = true)
    {
        DateTimeOffset iat = issuedAt ?? DateTimeOffset.UtcNow;
        DateTimeOffset? exp = hasExpiration ? expires ?? iat.AddMinutes(15) : null;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = iat.UtcDateTime,
            Expires = exp?.UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = subject, ["jti"] = Guid.NewGuid().ToString() },
            SigningCredentials = credentials ?? new SigningCredentials(TestSigningKey.Key.SecurityKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    private static string ForgeWithAnotherKey(string subject)
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Same "kid" as the real key, so only the signature itself can betray the forgery.
        ECDsaSecurityKey key = new(other) { KeyId = TestSigningKey.Key.SecurityKey.KeyId };
        return Forge(subject, credentials: new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256));
    }

    /// <summary>Changes the "sub" of a valid token but keeps its signature.</summary>
    private static string TamperPayload(string token)
    {
        string[] parts = token.Split('.');
        JsonNode payload = JsonNode.Parse(Base64Url.DecodeFromChars(parts[1]))!;
        payload["sub"] = Guid.CreateVersion7().ToString();
        return $"{parts[0]}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.{parts[2]}";
    }

    /// <summary>The payload of a valid token, with header {"alg":"none"} and no signature.</summary>
    private static string Unsigned(string token)
    {
        string header = Base64Url.EncodeToString("""{"alg":"none","typ":"JWT"}"""u8);
        return $"{header}.{token.Split('.')[1]}.";
    }

    private async Task<Account> FindByEmailAsync(string email)
    {
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        return await identity.Accounts.AsNoTracking().SingleAsync(account => account.Email == email, Cancellation);
    }

    private async Task<RefreshToken> FindStoredTokenAsync(string refreshToken)
    {
        byte[] hash = RefreshTokenValue.TryHash(refreshToken)!;
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        return await identity.RefreshTokens.AsNoTracking().SingleAsync(token => token.TokenHash == hash, Cancellation);
    }

    private async Task UpdateStoredTokenAsync(
        string refreshToken,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<RefreshToken>> setters)
    {
        byte[] hash = RefreshTokenValue.TryHash(refreshToken)!;
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        await identity.RefreshTokens.Where(token => token.TokenHash == hash).ExecuteUpdateAsync(setters, Cancellation);
    }

    /// <summary>Real Argon2id hasher that also counts verifications.</summary>
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private readonly Argon2idPasswordHasher _inner = new(Argon2idOptions.Default);
        private int _verifyCount;

        public int VerifyCount => _verifyCount;

        public string Hash(string password) => _inner.Hash(password);

        public bool Verify(string password, string hash)
        {
            Interlocked.Increment(ref _verifyCount);
            return _inner.Verify(password, hash);
        }

        public bool NeedsRehash(string hash) => _inner.NeedsRehash(hash);
    }
}
