using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eudoria.Core.Api.Tests;

/// <summary>POST /v1/accounts against the real core and a real PostgreSQL database.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class RegistrationTests(PostgresFixture database)
{
    private const string AcceptedBody = """{"status":"accepted"}""";
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidRegistration_Returns202_AndStoresTheAccountWithoutThePassword()
    {
        string pseudo = UniquePseudo();
        string email = UniqueEmail();
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RegisterAsync(client, $"  {email.ToUpperInvariant()} ", pseudo, Password);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AcceptedBody, await response.Content.ReadAsStringAsync(Cancellation));

        Account account = await FindByPseudoAsync(pseudo) ?? throw new InvalidOperationException("Account not stored.");
        Assert.Equal(email, account.Email); // trimmed and lower-cased
        Assert.Equal(pseudo, account.Pseudo); // as typed
        Assert.Equal(pseudo.ToLowerInvariant(), account.PseudoNormalized);
        Assert.Equal(7, account.Id.Version); // UUID v7
        Assert.InRange(account.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", account.PasswordHash, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, account.PasswordHash, StringComparison.Ordinal);
        Assert.True(new Argon2idPasswordHasher(Argon2idOptions.Default).Verify(Password, account.PasswordHash));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TakenPseudo_Returns409_RegardlessOfLetterCase(bool changeCase)
    {
        string pseudo = UniquePseudo();
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await RegisterAsync(client, UniqueEmail(), pseudo, Password);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        string again = changeCase ? pseudo.ToUpperInvariant() : pseudo;
        using HttpResponseMessage second = await RegisterAsync(client, UniqueEmail(), again, Password);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"type":"about:blank","title":"Conflict","status":409,"errors":[{"field":"pseudo","code":"pseudo_taken"}]}""",
            await second.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsedEmail_GetsTheSameAnswerAsASuccess_AndCreatesNoAccount(bool changeCase)
    {
        string email = UniqueEmail();
        string secondPseudo = UniquePseudo();
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await RegisterAsync(client, email, UniquePseudo(), Password);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        using HttpResponseMessage second = await RegisterAsync(client, changeCase ? email.ToUpperInvariant() : email, secondPseudo, Password);

        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(first.Content.Headers.ContentType?.ToString(), second.Content.Headers.ContentType?.ToString());
        Assert.Equal(await first.Content.ReadAsStringAsync(Cancellation), await second.Content.ReadAsStringAsync(Cancellation));
        Assert.Null(await FindByPseudoAsync(secondPseudo));
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        Assert.Equal(1, await identity.Accounts.CountAsync(account => account.Email == email, Cancellation));
    }

    [Fact]
    public async Task UsedEmail_StillHashesThePassword_SoItTakesAsLongAsASuccess()
    {
        string email = UniqueEmail();
        CountingPasswordHasher hasher = new();
        await using CoreApiFactory factory = new(database.ConnectionString,
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IPasswordHasher>(hasher)));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await RegisterAsync(client, email, UniquePseudo(), Password);
        Assert.Equal(1, hasher.HashCount);

        using HttpResponseMessage second = await RegisterAsync(client, email, UniquePseudo(), Password);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(2, hasher.HashCount);
    }

    [Fact]
    public async Task SeveralInvalidFields_Return400_WithEveryErrorAtOnce()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RegisterAsync(client, "not-an-email", "ab", "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"type":"about:blank","title":"Bad Request","status":400,"errors":[{"field":"email","code":"email_invalid"},{"field":"pseudo","code":"pseudo_too_short"},{"field":"password","code":"password_too_short"}]}""",
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"email":null,"pseudo":"","password":null}""")]
    public async Task MissingFields_Return400_WithRequiredCodes(string body)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["email_required", "pseudo_required", "password_required"], await ErrorCodesAsync(response));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("""{"email":"a@b.cd","pseudo":"Ana_42","password":"a-long-enough-passphrase","admin":true}""")]
    [InlineData("""{"email":"a@b.cd","pseudo":"Ana_42","pseudo":"Bob_42","password":"a-long-enough-passphrase"}""")]
    [InlineData("""{"email":"a@b.cd","pseudo":42,"password":"a-long-enough-passphrase"}""")]
    [InlineData("""{"Email":"a@b.cd","pseudo":"Ana_42","password":"a-long-enough-passphrase"}""")]
    [InlineData("""{"email":"a@b.cd","pseudo":"Ana_42","password":"a-long-enough-\ud800-passphrase"}""")]
    public async Task UnreadableBody_Returns400_BodyInvalid(string body)
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["body_invalid"], await ErrorCodesAsync(response));
    }

    [Fact]
    public async Task WrongContentType_Returns400_BodyInvalid()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();
        using StringContent content = new(RegistrationJson(UniqueEmail(), UniquePseudo(), Password), Encoding.UTF8, "text/plain");

        using HttpResponseMessage response = await client.PostAsync(new Uri("/v1/accounts", UriKind.Relative), content, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["body_invalid"], await ErrorCodesAsync(response));
    }

    [Fact]
    public async Task OversizedBody_Returns400_BodyInvalid()
    {
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RegisterAsync(client, UniqueEmail(), UniquePseudo(), new string('x', 10_000));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["body_invalid"], await ErrorCodesAsync(response));
    }

    [Fact]
    public async Task SimultaneousRegistrations_OfTheSamePseudo_CreateExactlyOneAccount()
    {
        string pseudo = UniquePseudo();
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => RegisterAsync(client, UniqueEmail(), pseudo, Password)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        string pseudoNormalized = pseudo.ToLowerInvariant();
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        Assert.Equal(1, await identity.Accounts.CountAsync(account => account.PseudoNormalized == pseudoNormalized, Cancellation));
        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task SimultaneousRegistrations_OfTheSameEmail_AllAnswerAccepted_AndCreateOneAccount()
    {
        string email = UniqueEmail();
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => RegisterAsync(client, email, UniquePseudo(), Password)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        Assert.Equal(1, await identity.Accounts.CountAsync(account => account.Email == email, Cancellation));
        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Password_IsNormalizedToNfkc_BeforeHashing()
    {
        // "é" typed as one character on one system, as "e" + combining accent on another,
        // and full-width digits from an Asian keyboard layout: one and the same password.
        const string decomposed = "café-au-lait-１２３";
        const string composed = "café-au-lait-123";
        string pseudo = UniquePseudo();
        await using CoreApiFactory factory = new(database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RegisterAsync(client, UniqueEmail(), pseudo, decomposed);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Account account = await FindByPseudoAsync(pseudo) ?? throw new InvalidOperationException("Account not stored.");
        Assert.True(new Argon2idPasswordHasher(Argon2idOptions.Default).Verify(composed, account.PasswordHash));
    }

    [Fact]
    public async Task Logs_NeverContainThePasswordOrTheEmail()
    {
        const string secretPassword = "Sup3r-Secret-Passphrase!";
        string email = UniqueEmail();
        CapturingLoggerProvider logs = new();
        await using CoreApiFactory factory = new(database.ConnectionString, logs);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage created = await RegisterAsync(client, email, UniquePseudo(), secretPassword);
        using HttpResponseMessage emailUsed = await RegisterAsync(client, email, UniquePseudo(), secretPassword);
        using HttpResponseMessage invalid = await RegisterAsync(client, email, "x", secretPassword);

        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Contains(logs.Entries, entry => entry.Contains("created", StringComparison.Ordinal)); // logging is on
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(secretPassword, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Model_HasNoChangeWithoutAMigration()
    {
        await using IdentityDbContext identity = database.CreateIdentityDbContext();

        Assert.False(identity.Database.HasPendingModelChanges(),
            "The Identity model changed without a migration: run 'dotnet ef migrations add <Name> --project core/src/Eudoria.Core.Identity --output-dir Persistence/Migrations'.");
        Assert.Empty(await identity.Database.GetPendingMigrationsAsync(Cancellation));
    }

    internal static string UniquePseudo() => "T" + Guid.NewGuid().ToString("N")[..12];

    internal static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.test";

    internal static string RegistrationJson(string email, string pseudo, string password) =>
        new JsonObject { ["email"] = email, ["pseudo"] = pseudo, ["password"] = password }.ToJsonString();

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string pseudo, string password) =>
        PostAsync(client, RegistrationJson(email, pseudo, password));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri("/v1/accounts", UriKind.Relative), content, Cancellation);
    }

    private static async Task<string[]> ErrorCodesAsync(HttpResponseMessage response)
    {
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancellation))!;
        return [.. body["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>())];
    }

    private async Task<Account?> FindByPseudoAsync(string pseudo)
    {
        string pseudoNormalized = pseudo.ToLowerInvariant();
        await using IdentityDbContext identity = database.CreateIdentityDbContext();
        return await identity.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(account => account.PseudoNormalized == pseudoNormalized, Cancellation);
    }

    /// <summary>Real Argon2id hasher that also counts how many hashes were computed.</summary>
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private readonly Argon2idPasswordHasher _inner = new(Argon2idOptions.Default);
        private int _hashCount;

        public int HashCount => _hashCount;

        public string Hash(string password)
        {
            Interlocked.Increment(ref _hashCount);
            return _inner.Hash(password);
        }

        public bool Verify(string password, string hash) => _inner.Verify(password, hash);
    }
}
