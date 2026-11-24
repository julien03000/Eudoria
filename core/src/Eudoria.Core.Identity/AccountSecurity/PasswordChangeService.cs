using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Http;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Eudoria.Core.Identity.Sessions;
using Eudoria.Core.Identity.Throttling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using static Eudoria.Core.Identity.Accounts.AccountRules;

namespace Eudoria.Core.Identity.AccountSecurity;

internal enum PasswordChangeOutcome
{
    Changed,
    Invalid,
    WrongCurrentPassword,
    Throttled,
    AccountNotFound,
}

internal sealed record PasswordChangeResult(
    PasswordChangeOutcome Outcome, TokenPair? Tokens = null, IReadOnlyList<FieldError>? Errors = null, TimeSpan? RetryAfter = null);

/// <summary>
/// Changes a password (plan 1.4, section 5). On success every session of the account is revoked,
/// earlier access tokens are refused, and a new session is opened for the caller (owner decision,
/// point 6). Wrong current passwords are limited like logins, per account and client address.
/// Never logs a password.
/// </summary>
internal sealed partial class PasswordChangeService(
    IdentityDbContext database,
    IPasswordHasher passwordHasher,
    PasswordHashingGate hashingGate,
    CommonPasswords commonPasswords,
    LoginThrottle throttle,
    SessionService sessions,
    TimeProvider time,
    ILogger<PasswordChangeService> logger)
{
    /// <summary>Counter scope of wrong current passwords (<see cref="LoginThrottle"/>).</summary>
    public const string Scope = "password-change";

    public async Task<PasswordChangeResult> ChangeAsync(
        Guid accountId, string? currentPassword, string? newPassword, string clientKey, CancellationToken cancellationToken)
    {
        using ThrottledAttempt attempt = throttle.TryBegin(Scope, accountId.ToString(), clientKey);
        if (!attempt.Allowed)
        {
            return new PasswordChangeResult(PasswordChangeOutcome.Throttled, RetryAfter: attempt.RetryAfter);
        }

        var account = await database.Accounts.AsNoTracking()
            .Where(candidate => candidate.Id == accountId)
            .Select(candidate => new { candidate.Email, candidate.Pseudo, candidate.PasswordHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return new PasswordChangeResult(PasswordChangeOutcome.AccountNotFound);
        }

        // The new password is checked first: no hashing for a request refused anyway.
        List<FieldError> errors = [];
        string? current = NormalizePassword(currentPassword ?? string.Empty);
        if (string.IsNullOrEmpty(currentPassword))
        {
            errors.Add(new FieldError("currentPassword", ErrorCodes.PasswordRequired));
        }

        string? next = NormalizePassword(newPassword ?? string.Empty);
        string? newPasswordError = next is null ? ErrorCodes.BodyInvalid : ValidatePassword(next);
        if (newPasswordError is null && commonPasswords.IsRefused(next!, account.Pseudo, account.Email))
        {
            newPasswordError = ErrorCodes.PasswordTooCommon;
        }

        if (newPasswordError is not null)
        {
            errors.Add(new FieldError("newPassword", newPasswordError));
        }

        if (errors.Count > 0 || current is null || next is null)
        {
            return new PasswordChangeResult(PasswordChangeOutcome.Invalid, Errors: errors.Count > 0
                ? errors
                : [new FieldError("currentPassword", ErrorCodes.BodyInvalid)]);
        }

        string newHash;
        using (await hashingGate.EnterAsync(cancellationToken))
        {
            bool verified = current.Length <= SessionService.MaxLoginPasswordLength
                && passwordHasher.Verify(current, account.PasswordHash);
            if (!verified)
            {
                attempt.Failed();
                LogWrongCurrentPassword(logger, accountId);
                return new PasswordChangeResult(PasswordChangeOutcome.WrongCurrentPassword);
            }

            attempt.Succeeded();
            if (string.Equals(current, next, StringComparison.Ordinal))
            {
                return new PasswordChangeResult(PasswordChangeOutcome.Invalid,
                    Errors: [new FieldError("newPassword", ErrorCodes.PasswordUnchanged)]);
            }

            newHash = passwordHasher.Hash(next);
        }

        // One transaction: new hash (only if unchanged meanwhile), every session revoked, earlier
        // access tokens refused, and a new session for this device. The update locks the account row.
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset validAfter = SessionService.CeilingToSecond(now);
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        int updated = await database.Accounts
            .Where(candidate => candidate.Id == accountId && candidate.PasswordHash == account.PasswordHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.PasswordHash, newHash), cancellationToken);
        if (updated == 0)
        {
            // Changed by a simultaneous request: the current password checked here is no longer valid.
            await transaction.RollbackAsync(cancellationToken);
            return new PasswordChangeResult(PasswordChangeOutcome.WrongCurrentPassword);
        }

        await sessions.MoveTokensValidAfterAsync(accountId, validAfter, cancellationToken);
        int revoked = await sessions.RevokeAccountSessionsAsync(accountId, now, cancellationToken);
        TokenPair pair = await sessions.OpenSessionAsync(accountId, validAfter, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogPasswordChanged(logger, accountId, revoked);
        return new PasswordChangeResult(PasswordChangeOutcome.Changed, Tokens: pair);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Password of account {AccountId} changed; {RevokedCount} active token(s) revoked, new session opened.")]
    private static partial void LogPasswordChanged(ILogger logger, Guid accountId, int revokedCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password change refused for account {AccountId}: wrong current password.")]
    private static partial void LogWrongCurrentPassword(ILogger logger, Guid accountId);
}
