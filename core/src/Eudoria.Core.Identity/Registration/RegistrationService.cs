using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Http;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Eudoria.Core.Identity.Accounts.AccountRules;

namespace Eudoria.Core.Identity.Registration;

internal enum RegistrationOutcome
{
    /// <summary>Account created, or e-mail already in use (indistinguishable on purpose).</summary>
    Accepted,
    Invalid,
    PseudoTaken,
}

internal sealed record RegistrationResult(RegistrationOutcome Outcome, IReadOnlyList<FieldError> Errors)
{
    public static readonly RegistrationResult Accepted = new(RegistrationOutcome.Accepted, []);
    public static readonly RegistrationResult PseudoTaken =
        new(RegistrationOutcome.PseudoTaken, [new FieldError("pseudo", ErrorCodes.PseudoTaken)]);

    public static RegistrationResult Invalid(IReadOnlyList<FieldError> errors) => new(RegistrationOutcome.Invalid, errors);
}

/// <summary>Creates accounts. Never logs the e-mail or the password.</summary>
internal sealed partial class RegistrationService(
    IdentityDbContext database,
    IPasswordHasher passwordHasher,
    CommonPasswords commonPasswords,
    PasswordHashingGate hashingGate,
    TimeProvider time,
    ILogger<RegistrationService> logger)
{
    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken)
    {
        List<FieldError> errors = [];

        string email = NormalizeEmail(request.Email ?? string.Empty);
        AddError(errors, "email", ValidateEmail(email));

        string pseudo = request.Pseudo ?? string.Empty;
        AddError(errors, "pseudo", ValidatePseudo(pseudo));

        string? password = NormalizePassword(request.Password ?? string.Empty);
        string? passwordError = password is null ? ErrorCodes.BodyInvalid : ValidatePassword(password);
        if (passwordError is null && commonPasswords.IsRefused(password!, pseudo, email))
        {
            passwordError = ErrorCodes.PasswordTooCommon;
        }

        AddError(errors, "password", passwordError);

        if (errors.Count > 0 || password is null)
        {
            return RegistrationResult.Invalid(errors);
        }

        // The pseudo is public: checking it first reveals nothing, and must come before the e-mail
        // so that "taken pseudo + used e-mail" and "taken pseudo + new e-mail" answer the same.
        string pseudoNormalized = NormalizePseudo(pseudo);
        if (await IsPseudoTakenAsync(pseudoNormalized, cancellationToken))
        {
            return RegistrationResult.PseudoTaken;
        }

        // Same queries and same duration whether the e-mail is new or already used:
        // hash, one insert that the database skips on any duplicate (no exception, so nothing is
        // logged as an error), then the pseudo check again, in every case.
        DateTimeOffset now = time.GetUtcNow();
        Guid id = Guid.CreateVersion7(now);
        string passwordHash;
        using (await hashingGate.EnterAsync(cancellationToken))
        {
            passwordHash = passwordHasher.Hash(password);
        }

        bool inserted = await TryInsertAsync(id, email, pseudo, pseudoNormalized, passwordHash, now, cancellationToken);

        // Only matters when nothing was inserted: a simultaneous registration took the pseudo.
        bool pseudoTaken = await IsPseudoTakenByAnotherAsync(pseudoNormalized, id, cancellationToken);
        if (inserted)
        {
            LogAccountCreated(logger, id);
            return RegistrationResult.Accepted;
        }

        if (pseudoTaken)
        {
            return RegistrationResult.PseudoTaken;
        }

        LogEmailAlreadyUsed(logger);
        return RegistrationResult.Accepted;
    }

    /// <summary>
    /// INSERT … ON CONFLICT DO NOTHING: the unique indexes on the e-mail and the pseudo are checked
    /// by the database within the insert itself (no gap between check and insert, even under
    /// simultaneous registrations). Parameterized: the interpolated values become SQL parameters.
    /// </summary>
    private async Task<bool> TryInsertAsync(
        Guid id, string email, string pseudo, string pseudoNormalized, string passwordHash, DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        List<Guid> inserted = await database.Database.SqlQuery<Guid>($"""
            INSERT INTO identity.accounts (id, email, pseudo, pseudo_normalized, password_hash, created_at)
            VALUES ({id}, {email}, {pseudo}, {pseudoNormalized}, {passwordHash}, {createdAt})
            ON CONFLICT DO NOTHING
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);
        return inserted.Count == 1;
    }

    private Task<bool> IsPseudoTakenByAnotherAsync(string pseudoNormalized, Guid id, CancellationToken cancellationToken) =>
        database.Accounts.AsNoTracking()
            .AnyAsync(account => account.PseudoNormalized == pseudoNormalized && account.Id != id, cancellationToken);

    private Task<bool> IsPseudoTakenAsync(string pseudoNormalized, CancellationToken cancellationToken) =>
        database.Accounts.AsNoTracking().AnyAsync(account => account.PseudoNormalized == pseudoNormalized, cancellationToken);

    private static void AddError(List<FieldError> errors, string field, string? code)
    {
        if (code is not null)
        {
            errors.Add(new FieldError(field, code));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} created.")]
    private static partial void LogAccountCreated(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registration ignored: e-mail already in use (answered as accepted).")]
    private static partial void LogEmailAlreadyUsed(ILogger logger);
}
