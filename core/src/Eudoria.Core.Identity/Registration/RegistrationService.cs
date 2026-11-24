using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Http;
using Eudoria.Core.Identity.Passwords;
using Eudoria.Core.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
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
    IdentityDbContext database, IPasswordHasher passwordHasher, TimeProvider time, ILogger<RegistrationService> logger)
{
    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken)
    {
        List<FieldError> errors = [];

        string email = NormalizeEmail(request.Email ?? string.Empty);
        AddError(errors, "email", ValidateEmail(email));

        string pseudo = request.Pseudo ?? string.Empty;
        AddError(errors, "pseudo", ValidatePseudo(pseudo));

        string? password = NormalizePassword(request.Password ?? string.Empty);
        AddError(errors, "password", password is null ? ErrorCodes.BodyInvalid : ValidatePassword(password));

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

        // Hashed in every remaining case, so an already used e-mail takes as long as a new one.
        Account account = new()
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            Email = email,
            Pseudo = pseudo,
            PseudoNormalized = pseudoNormalized,
            PasswordHash = passwordHasher.Hash(password),
            CreatedAt = time.GetUtcNow(),
        };

        database.Accounts.Add(account);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } violation)
        {
            // Lost a race with a simultaneous registration: same answers as the checks above.
            if (violation.ConstraintName == AccountConfiguration.PseudoIndex
                || await IsPseudoTakenAsync(pseudoNormalized, cancellationToken))
            {
                return RegistrationResult.PseudoTaken;
            }

            LogEmailAlreadyUsed(logger);
            return RegistrationResult.Accepted;
        }

        LogAccountCreated(logger, account.Id);
        return RegistrationResult.Accepted;
    }

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
