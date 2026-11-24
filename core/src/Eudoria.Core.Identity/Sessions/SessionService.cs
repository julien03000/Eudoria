using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Throttling;
using Eudoria.Core.Identity.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eudoria.Core.Identity.Sessions;

/// <summary>Body of a successful login or renewal (contract schema TokenPair).</summary>
internal sealed record TokenPair(string AccessToken, string TokenType, long ExpiresIn, string RefreshToken, long RefreshExpiresIn);

/// <summary>Outcome of a login: tokens, wrong credentials (both null), or too many attempts (<see cref="RetryAfter"/>).</summary>
internal sealed record LoginResult(TokenPair? Tokens, TimeSpan? RetryAfter)
{
    public static readonly LoginResult InvalidCredentials = new(null, null);
}

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
    PasswordHashingGate hashingGate,
    LoginThrottle throttle,
    AccessTokenIssuer accessTokens,
    IOptions<TokenOptions> options,
    TimeProvider time,
    ILogger<SessionService> logger)
{
    /// <summary>Longer passwords are refused without hashing (registration allows 128 code points).</summary>
    public const int MaxLoginPasswordLength = 1024;

    /// <summary>Counter scope of failed logins (<see cref="LoginThrottle"/>).</summary>
    public const string LoginScope = "login";

    /// <summary>
    /// Returns a token pair, wrong credentials (unknown e-mail and wrong password alike), or a
    /// refusal when too many attempts failed for this e-mail (blocked: the password is not checked).
    /// </summary>
    public async Task<LoginResult> LoginAsync(string? email, string? password, string clientKey, CancellationToken cancellationToken)
    {
        string normalizedEmail = AccountRules.NormalizeEmail(email ?? string.Empty);

        // Counted per e-mail whether it has an account or not: a 429 must not reveal that it exists.
        using ThrottledAttempt attempt = throttle.TryBegin(LoginScope, normalizedEmail, clientKey);
        if (!attempt.Allowed)
        {
            LogLoginThrottled(logger);
            return new LoginResult(null, attempt.RetryAfter);
        }

        string? normalizedPassword = AccountRules.NormalizePassword(password ?? string.Empty);
        if (normalizedPassword is null || normalizedPassword.Length > MaxLoginPasswordLength)
        {
            attempt.Failed();
            LogLoginFailed(logger);
            return LoginResult.InvalidCredentials;
        }

        var account = await database.Accounts.AsNoTracking()
            .Where(candidate => candidate.Email == normalizedEmail)
            .Select(candidate => new { candidate.Id, candidate.PasswordHash })
            .SingleOrDefaultAsync(cancellationToken);

        string currentHash;
        using (await hashingGate.EnterAsync(cancellationToken))
        {
            // Hashed in every case, so an unknown e-mail takes as long as a wrong password.
            bool verified = passwordHasher.Verify(normalizedPassword, account?.PasswordHash ?? dummyHash.Value);
            if (account is null || !verified)
            {
                attempt.Failed();
                LogLoginFailed(logger);
                return LoginResult.InvalidCredentials;
            }

            attempt.Succeeded();
            currentHash = passwordHasher.NeedsRehash(account.PasswordHash)
                ? await RehashAsync(account.Id, account.PasswordHash, normalizedPassword, cancellationToken)
                : account.PasswordHash;
        }

        // The password may have been changed since it was checked: open the session only if the
        // account still has the hash that was verified (row locked against a simultaneous change).
        DateTimeOffset now = time.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        AccountLock? locked = await LockAccountAsync(account.Id, cancellationToken);
        if (locked is null || locked.PasswordHash != currentHash)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLoginFailed(logger);
            return LoginResult.InvalidCredentials;
        }

        TokenPair pair = await OpenSessionAsync(account.Id, IssuedAt(now, locked), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogLoggedIn(logger, account.Id);
        return new LoginResult(pair, null);
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
        // The account row is locked first: a simultaneous "revoke all" (which updates that row) either
        // waits for this renewal and then revokes the new token too, or runs first and this renewal
        // finds its token revoked.
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        AccountLock? locked = await LockAccountAsync(token.AccountId, cancellationToken);
        if (locked is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        int consumed = await database.RefreshTokens
            .Where(candidate => candidate.Id == token.Id && candidate.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevokedAt, now), cancellationToken);
        if (consumed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeReusedSessionAsync(token, now, cancellationToken);
            return null;
        }

        TokenPair pair = await IssueAsync(token.AccountId, token.FamilyId, token.SessionExpiresAt, IssuedAt(now, locked), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pair;
    }

    /// <summary>
    /// "Sign out every device": revokes every session of the account and refuses every access token
    /// issued so far. Returns false when the account does not exist.
    /// </summary>
    public async Task<bool> RevokeAllAsync(Guid accountId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        int updated = await MoveTokensValidAfterAsync(accountId, CeilingToSecond(now), cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        int revoked = await RevokeAccountSessionsAsync(accountId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogAllSessionsRevoked(logger, accountId, revoked);
        return true;
    }

    /// <summary>
    /// Opens a new session (refresh token family) whose tokens are issued at <paramref name="issuedAt"/>.
    /// Call it inside a transaction that holds the account row (<see cref="LockAccountAsync"/>, or an update).
    /// </summary>
    internal Task<TokenPair> OpenSessionAsync(Guid accountId, DateTimeOffset issuedAt, CancellationToken cancellationToken) =>
        IssueAsync(accountId, familyId: Guid.CreateVersion7(issuedAt), issuedAt + options.Value.SessionLifetime, issuedAt, cancellationToken);

    /// <summary>
    /// Moves the account's "tokens valid after" date forward (never backward); the update locks the
    /// account row until the end of the transaction. Returns the number of rows updated (0: no account).
    /// </summary>
    internal Task<int> MoveTokensValidAfterAsync(Guid accountId, DateTimeOffset validAfter, CancellationToken cancellationToken) =>
        database.Accounts
            .Where(account => account.Id == accountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                account => account.TokensValidAfter,
                account => account.TokensValidAfter == null || account.TokensValidAfter < validAfter ? validAfter : account.TokensValidAfter),
                cancellationToken);

    /// <summary>Revokes every still-valid refresh token of the account. Returns how many.</summary>
    internal Task<int> RevokeAccountSessionsAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.RefreshTokens
            .Where(candidate => candidate.AccountId == accountId && candidate.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevokedAt, now), cancellationToken);

    /// <summary>
    /// Access tokens carry "iat" in whole seconds: "valid after" dates are rounded up to the next
    /// second, so a token issued in the same second as a revocation is refused too.
    /// </summary>
    internal static DateTimeOffset CeilingToSecond(DateTimeOffset value)
    {
        long remainder = value.UtcTicks % TimeSpan.TicksPerSecond;
        return remainder == 0 ? value : value.AddTicks(TimeSpan.TicksPerSecond - remainder);
    }

    /// <summary>Tokens are never issued before the account's "valid after" date (else they would be refused at once).</summary>
    private static DateTimeOffset IssuedAt(DateTimeOffset now, AccountLock locked) =>
        locked.TokensValidAfter is { } validAfter && validAfter > now ? validAfter : now;

    /// <summary>Reads the account row and locks it (FOR SHARE) until the end of the current transaction.</summary>
    private async Task<AccountLock?> LockAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        List<AccountLock> rows = await database.Database.SqlQuery<AccountLock>($"""
            SELECT password_hash AS "PasswordHash", tokens_valid_after AS "TokensValidAfter"
            FROM identity.accounts
            WHERE id = {accountId}
            FOR SHARE
            """).ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
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
    /// Returns the hash the account should now have (the new one, or the old one if not replaced).
    /// </summary>
    private async Task<string> RehashAsync(Guid accountId, string oldHash, string password, CancellationToken cancellationToken)
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
                return newHash;
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            LogRehashFailed(logger, accountId, exception);
        }

        return oldHash;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} logged in.")]
    private static partial void LogLoggedIn(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login failed: unknown e-mail or wrong password.")]
    private static partial void LogLoginFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login refused: too many failed attempts for this e-mail (not checked).")]
    private static partial void LogLoginThrottled(ILogger logger);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Every session of account {AccountId} revoked ({RevokedCount} active token(s)); earlier access tokens refused.")]
    private static partial void LogAllSessionsRevoked(ILogger logger, Guid accountId, int revokedCount);
}

/// <summary>The account row as read under lock: its password hash and its "tokens valid after" date.</summary>
internal sealed class AccountLock
{
    public required string PasswordHash { get; init; }

    public DateTimeOffset? TokensValidAfter { get; init; }
}
