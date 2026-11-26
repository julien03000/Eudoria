using Eudoria.Core.Identity.AccountSecurity;
using Eudoria.Core.Identity.EntryTickets;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Profile;
using Eudoria.Core.Identity.Registration;
using Eudoria.Core.Identity.Sessions;
using Eudoria.Core.Identity.Throttling;
using Eudoria.Core.Identity.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Eudoria.Core.Identity;

/// <summary>
/// Entry point of the Identity module (accounts, sessions, tokens). The host only calls these two
/// methods (plus the standard UseAuthentication/UseAuthorization); everything else is internal.
/// </summary>
public static class IdentityModule
{
    internal const string InvalidSigningKeyMessage =
        "Missing or invalid access-token signing key 'Identity:Tokens:SigningKey' (expected: ECDSA P-256 private key, PKCS#8, Base64). " +
        "Generate it once with tools/dev/new-signing-key.ps1, then run the core with tools/dev/run-core.ps1.";

    internal const string InvalidEntryTicketKeyMessage =
        "Missing or invalid entry ticket signing key 'Identity:EntryTickets:SigningKey' (expected: ECDSA P-256 private key, PKCS#8, Base64). " +
        "Generate it once with tools/dev/new-signing-key.ps1 -EntryTickets, then run the core with tools/dev/run-core.ps1.";

    internal const string SameSigningKeysMessage =
        "The entry ticket signing key must differ from the access-token signing key (ADR-0007). " +
        "Generate a new one with tools/dev/new-signing-key.ps1 -EntryTickets -Force.";

    /// <summary>
    /// Registers the module's services. The host must also call <c>UseRateLimiter()</c> (limits per
    /// client address, plan 1.4). Requires the host to register the shared
    /// <see cref="NpgsqlDataSource"/> (one connection pool for the whole core).
    /// The core refuses to start without its two valid, distinct signing keys (access tokens, entry tickets).
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>(), ConfigureNpgsql));
        services.AddSingleton(Argon2idOptions.Default);
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<DummyPasswordHash>();
        services.AddSingleton<CommonPasswords>();
        services.AddSingleton<PasswordHashingGate>();
        services.AddSingleton<LoginThrottle>();

        services.AddOptions<ThrottlingOptions>()
            .BindConfiguration(ThrottlingOptions.SectionName)
            .Validate(options => options.IsValid(), "Invalid 'Identity:Throttling' settings.")
            .ValidateOnStart();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<ThrottlingOptions>>((limiter, throttling) => RateLimitPolicies.Configure(limiter, throttling.Value));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RegistrationService>();
        services.AddScoped<SessionService>();
        services.AddScoped<PasswordChangeService>();

        services.AddOptions<TokenOptions>()
            .BindConfiguration(TokenOptions.SectionName)
            .Validate(options => TokenSigningKey.IsValid(options.SigningKey), InvalidSigningKeyMessage)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer) && !string.IsNullOrWhiteSpace(options.Audience)
                    && options.AccessTokenLifetime > TimeSpan.Zero && options.RefreshTokenLifetime > TimeSpan.Zero
                    && options.SessionLifetime >= options.RefreshTokenLifetime,
                "Invalid 'Identity:Tokens' settings (issuer, audience or lifetimes).")
            .ValidateOnStart();
        services.AddSingleton(serviceProvider =>
            TokenSigningKey.TryCreate(serviceProvider.GetRequiredService<IOptions<TokenOptions>>().Value.SigningKey)
            ?? throw new InvalidOperationException(InvalidSigningKeyMessage));
        services.AddSingleton<AccessTokenIssuer>();

        // World entry tickets (ADR-0007): their own key, never the access-token key.
        services.AddOptions<EntryTicketOptions>()
            .BindConfiguration(EntryTicketOptions.SectionName)
            .Validate(options => TokenSigningKey.IsValid(options.SigningKey), InvalidEntryTicketKeyMessage)
            .Validate<IOptions<TokenOptions>>(
                (options, tokens) => !TokenSigningKey.AreSameKey(options.SigningKey, tokens.Value.SigningKey),
                SameSigningKeysMessage)
            .Validate(options => options.IsValid(), "Invalid 'Identity:EntryTickets' settings (lifetime or known worlds).")
            .ValidateOnStart();
        services.AddSingleton(serviceProvider => new EntryTicketSigningKey(
            TokenSigningKey.TryCreate(serviceProvider.GetRequiredService<IOptions<EntryTicketOptions>>().Value.SigningKey, KeyIdStyle.Rfc7638Thumbprint)
            ?? throw new InvalidOperationException(InvalidEntryTicketKeyMessage)));
        services.AddSingleton<EntryTicketIssuer>();
        services.AddSingleton<IKnownWorlds, ConfiguredKnownWorlds>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenSigningKey, IOptions<TokenOptions>>(ConfigureBearer);
        services.AddAuthorization();
        return services;
    }

    /// <summary>Maps the module's public routes under the given version group (e.g. /v1).</summary>
    public static RouteGroupBuilder MapIdentityEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/accounts", RegistrationEndpoint.RegisterAsync)
            .RequireRateLimiting(RateLimitPolicies.Registration).WithHashingBusyAnswer();
        api.MapPost("/auth/login", SessionEndpoints.LoginAsync)
            .RequireRateLimiting(RateLimitPolicies.Login).WithHashingBusyAnswer();
        api.MapPost("/auth/refresh", SessionEndpoints.RefreshAsync)
            .RequireRateLimiting(RateLimitPolicies.Refresh);
        api.MapPost("/auth/logout", SessionEndpoints.LogoutAsync);
        api.MapGet("/me", MeEndpoint.GetAsync).RequireAuthorization();
        api.MapPost("/worlds/{worldId}/entry-tickets", EntryTicketEndpoints.Issue)
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.EntryTicket);
        api.MapGet("/entry-tickets/keys", EntryTicketEndpoints.GetKeys)
            .RequireRateLimiting(RateLimitPolicies.TicketKeys);
        api.MapPost("/me/password", AccountSecurityEndpoints.ChangePasswordAsync)
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Account).WithHashingBusyAnswer();
        api.MapPost("/me/sessions/revoke-all", AccountSecurityEndpoints.RevokeAllSessionsAsync)
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Account);
        return api;
    }

    /// <summary>The module keeps its migration history in its own schema.</summary>
    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTable, IdentityDbContext.Schema);

    /// <summary>
    /// Strict access-token validation: ES256 only, type "eudoria-access+jwt" only, our key, our issuer, audience "eudoria-core",
    /// signed and expiring tokens required, 30 s clock tolerance; claims kept as issued ("sub").
    /// Then tokens issued before a password change or "revoke all" are refused (<see cref="TokenRevocationCheck"/>).
    /// </summary>
    private static void ConfigureBearer(JwtBearerOptions bearer, TokenSigningKey signingKey, IOptions<TokenOptions> options)
    {
        bearer.MapInboundClaims = false;
        bearer.Events = new JwtBearerEvents { OnTokenValidated = TokenRevocationCheck.OnTokenValidatedAsync };
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Value.Issuer,
            ValidAudience = options.Value.Audience,
            IssuerSigningKey = signingKey.SecurityKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = [TokenTypes.Access],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
    }
}
