using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eudoria.Core.Identity.Sessions;

/// <summary>Body of a successful login or renewal (contract schema TokenPair).</summary>
internal sealed record TokenPair(string AccessToken, string TokenType, long ExpiresIn, string RefreshToken, long RefreshExpiresIn);

/// <summary>
/// A hash of a random password at the current cost, computed once. Verifying against it when the
/// e-mail is unknown makes that case take as long as a wrong password on a real account.
/// </summary>
internal sealed class DummyPasswordHash(IPasswordHasher passwordHasher)
{
    private readonly Lazy<string> _hash = new(() => passwordHasher.Hash(Guid.NewGuid().ToString()));

    public string Value => _hash.Value;
}

/// <summary>
/// Login, renewal and logout. Never logs the e-mail, the password or any token.
/// Refresh tokens are rotated on every use; presenting a consumed one revokes the whole session
/// (strict version, owner decision of plan 1.3).
/// </summary>
internal sealed partial class SessionService(
    IdentityDbContext database,
    IPasswordHasher passwordHasher,
    DummyPasswordHash dummyHash,
    AccessTokenIssuer accessTokens,
    IOptions<TokenOptions> options,
    TimeProvider time,
    ILogger<SessionService> logger)
{
    /// <summary>Longer passwords are refused without hashing (registration allows 128 code points).</summary>
    public const int MaxLoginPasswordLength = 1024;

    /// <summary>Returns a token pair, or null for wrong credentials (unknown e-mail and wrong password alike).</summary>
    public async Task<TokenPair?> LoginAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        string normalizedEmail = AccountRules.NormalizeEmail(email ?? string.Empty);
        string? normalizedPassword = AccountRules.NormalizePassword(password ?? string.Empty);
        if (normalizedPassword is null || normalizedPassword.Length > MaxLoginPasswordLength)
        {
            LogLoginFailed(logger);
            return null;
        }

        var account = await database.Accounts.AsNoTracking()
            .Where(candidate => candidate.Email == normalizedEmail)
            .Select(candidate => new { candidate.Id, candidate.PasswordHash })
            .SingleOrDefaultAsync(cancellationToken);

        // Hashed in every case, so an unknown e-mail takes as long as a wrong password.
        bool verified = passwordHasher.Verify(normalizedPassword, account?.PasswordHash ?? dummyHash.Value);
        if (account is null || !verified)
        {
            LogLoginFailed(logger);
            return null;
        }

        if (passwordHasher.NeedsRehash(account.PasswordHash))
        {
            await RehashAsync(account.Id, account.PasswordHash, normalizedPassword, cancellationToken);
        }

        DateTimeOffset now = time.GetUtcNow();
        TokenPair pair = await IssueAsync(account.Id, familyId: Guid.CreateVersion7(now), now + options.Value.SessionLifetime, now, cancellationToken);
        LogLoggedIn(logger, account.Id);
        return pair;
    }

    /// <summary>Consumes a refresh token and returns a new pair in the same session, or null when it is not usable.</summary>
    public async Task<TokenPair?> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        byte[]? hash = RefreshTokenValue.TryHash(refreshToken);
        if (hash is null)
        {
            return null;
        }

        RefreshToken? token = await database.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return null;
        }

        DateTimeOffset now = time.GetUtcNow();
        if (token.RevokedAt is not null)
        {
            await RevokeReusedSessionAsync(token, now, cancellationToken);
            return null;
        }

        if (now >= token.ExpiresAt || now >= token.SessionExpiresAt)
        {
            return null;
        }

        // Consuming the old token and storing the new one is atomic. A simultaneous renewal with the
        // same token waits on the row lock, then finds it consumed: it is a reuse (strict version).
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        int consumed = await database.RefreshTokens
            .Where(candidate => candidate.Id == token.Id && candidate.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevokedAt, now), cancellationToken);
        if (consumed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeReusedSessionAsync(token, now, cancellationToken);
            return null;
        }

        TokenPair pair = await IssueAsync(token.AccountId, token.FamilyId, token.SessionExpiresAt, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pair;
    }

    /// <summary>Revokes the session of a refresh token. Does nothing (silently) for an unknown token.</summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        byte[]? hash = RefreshTokenValue.TryHash(refreshToken);
        if (hash is null)
        {
            return;
        }

        RefreshToken? token = await database.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (token is not null)
        {
            await RevokeSessionAsync(token.FamilyId, time.GetUtcNow(), cancellationToken);
            LogLoggedOut(logger, token.AccountId);
        }
    }

    private async Task<TokenPair> IssueAsync(
        Guid accountId, Guid familyId, DateTimeOffset sessionExpiresAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        (string refreshToken, byte[] hash) = RefreshTokenValue.Generate();
        DateTimeOffset refreshExpiresAt = Min(now + options.Value.RefreshTokenLifetime, sessionExpiresAt);
        database.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(now),
            AccountId = accountId,
            FamilyId = familyId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt,
            SessionExpiresAt = sessionExpiresAt,
        });
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return new TokenPair(
            accessTokens.Issue(accountId, now),
            "Bearer",
            (long)accessTokens.Lifetime.TotalSeconds,
            refreshToken,
            (long)(refreshExpiresAt - now).TotalSeconds);
    }

    private async Task RevokeReusedSessionAsync(RefreshToken token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Only cutting a still-active session is a sign of theft; a token of a closed session
        // (logged out, or already revoked) is just refused.
        int revoked = await RevokeSessionAsync(token.FamilyId, now, cancellationToken);
        if (revoked > 0)
        {
            LogReuseDetected(logger, token.AccountId, token.FamilyId, revoked);
        }
        else
        {
            LogClosedSessionTokenPresented(logger, token.AccountId, token.FamilyId);
        }
    }

    private Task<int> RevokeSessionAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RefreshTokens
            .Where(candidate => candidate.FamilyId == familyId && candidate.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevokedAt, now), cancellationToken);

    /// <summary>
    /// Replaces a hash made with an older cost. A failure does not prevent the login: the old hash
    /// stays valid and the next login tries again. Only replaced if unchanged meanwhile.
    /// </summary>
    private async Task RehashAsync(Guid accountId, string oldHash, string password, CancellationToken cancellationToken)
    {
        try
        {
            string newHash = passwordHasher.Hash(password);
            int updated = await database.Accounts
                .Where(account => account.Id == accountId && account.PasswordHash == oldHash)
                .ExecuteUpdateAsync(setters => setters.SetProperty(account => account.PasswordHash, newHash), cancellationToken);
            if (updated > 0)
            {
                LogRehashed(logger, accountId);
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            LogRehashFailed(logger, accountId, exception);
        }
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} logged in.")]
    private static partial void LogLoggedIn(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login failed: unknown e-mail or wrong password.")]
    private static partial void LogLoginFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password hash of account {AccountId} upgraded to the current cost.")]
    private static partial void LogRehashed(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not upgrade the password hash of account {AccountId}; will retry at next login.")]
    private static partial void LogRehashFailed(ILogger logger, Guid accountId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Consumed refresh token reused for account {AccountId}: session {FamilyId} revoked ({RevokedCount} active token(s)). Possible token theft.")]
    private static partial void LogReuseDetected(ILogger logger, Guid accountId, Guid familyId, int revokedCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh token of closed session {FamilyId} (account {AccountId}) refused.")]
    private static partial void LogClosedSessionTokenPresented(ILogger logger, Guid accountId, Guid familyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} logged out.")]
    private static partial void LogLoggedOut(ILogger logger, Guid accountId);
}
