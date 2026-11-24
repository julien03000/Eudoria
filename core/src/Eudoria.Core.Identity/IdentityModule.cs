using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Profile;
using Eudoria.Core.Identity.Registration;
using Eudoria.Core.Identity.Sessions;
using Eudoria.Core.Identity.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
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

    /// <summary>
    /// Registers the module's services. Requires the host to register the shared
    /// <see cref="NpgsqlDataSource"/> (one connection pool for the whole core).
    /// The core refuses to start without a valid signing key.
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>(), ConfigureNpgsql));
        services.AddSingleton(Argon2idOptions.Default);
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<DummyPasswordHash>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RegistrationService>();
        services.AddScoped<SessionService>();

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

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenSigningKey, IOptions<TokenOptions>>(ConfigureBearer);
        services.AddAuthorization();
        return services;
    }

    /// <summary>Maps the module's public routes under the given version group (e.g. /v1).</summary>
    public static RouteGroupBuilder MapIdentityEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/accounts", RegistrationEndpoint.RegisterAsync);
        api.MapPost("/auth/login", SessionEndpoints.LoginAsync);
        api.MapPost("/auth/refresh", SessionEndpoints.RefreshAsync);
        api.MapPost("/auth/logout", SessionEndpoints.LogoutAsync);
        api.MapGet("/me", MeEndpoint.GetAsync).RequireAuthorization();
        return api;
    }

    /// <summary>The module keeps its migration history in its own schema.</summary>
    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTable, IdentityDbContext.Schema);

    /// <summary>
    /// Strict access-token validation: ES256 only, our key, our issuer, audience "eudoria-core",
    /// signed and expiring tokens required, 30 s clock tolerance; claims kept as issued ("sub").
    /// </summary>
    private static void ConfigureBearer(JwtBearerOptions bearer, TokenSigningKey signingKey, IOptions<TokenOptions> options)
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Value.Issuer,
            ValidAudience = options.Value.Audience,
            IssuerSigningKey = signingKey.SecurityKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = [JwtConstants.HeaderType],
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
