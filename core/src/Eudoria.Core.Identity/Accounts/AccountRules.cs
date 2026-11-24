using System.Text;

namespace Eudoria.Core.Identity.Accounts;

/// <summary>
/// Server-side rules and normalization of the registration fields
/// (contract: shared/api/core-v1.yaml, operation registerAccount).
/// </summary>
internal static class AccountRules
{
    public const int EmailMaxLength = 254;
    public const int PseudoMinLength = 3;
    public const int PseudoMaxLength = 20;
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 128;

    /// <summary>Trims and lower-cases an e-mail address.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Lower-cases a pseudo (pseudos are ASCII, see <see cref="ValidatePseudo"/>).</summary>
    public static string NormalizePseudo(string pseudo) => pseudo.ToLowerInvariant();

    /// <summary>
    /// Normalizes a password to Unicode NFKC (NIST SP 800-63B-4), so the same password typed on
    /// different keyboards or systems (composed or decomposed accents, full-width digits...)
    /// gives the same bytes. Returns null when the text is not valid Unicode.
    /// </summary>
    public static string? NormalizePassword(string password)
    {
        try
        {
            return password.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return null; // e.g. a lone surrogate
        }
    }

    /// <summary>Validates an already normalized e-mail. Returns an error code, or null when valid.</summary>
    public static string? ValidateEmail(string normalizedEmail)
    {
        if (normalizedEmail.Length == 0)
        {
            return ErrorCodes.EmailRequired;
        }

        if (normalizedEmail.Length > EmailMaxLength || normalizedEmail.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return ErrorCodes.EmailInvalid;
        }

        int at = normalizedEmail.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != normalizedEmail.LastIndexOf('@'))
        {
            return ErrorCodes.EmailInvalid;
        }

        string domain = normalizedEmail[(at + 1)..];
        bool domainOk = domain.IndexOf('.', StringComparison.Ordinal) > 0
            && !domain.EndsWith('.')
            && !domain.Contains("..", StringComparison.Ordinal);
        return domainOk ? null : ErrorCodes.EmailInvalid;
    }

    /// <summary>Validates a pseudo as typed: 3-20 of A-Z a-z 0-9 _ -, starting with a letter.</summary>
    public static string? ValidatePseudo(string pseudo)
    {
        if (pseudo.Length == 0)
        {
            return ErrorCodes.PseudoRequired;
        }

        if (!pseudo.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            return ErrorCodes.PseudoInvalidCharacters;
        }

        if (pseudo.Length < PseudoMinLength)
        {
            return ErrorCodes.PseudoTooShort;
        }

        if (pseudo.Length > PseudoMaxLength)
        {
            return ErrorCodes.PseudoTooLong;
        }

        return char.IsAsciiLetter(pseudo[0]) ? null : ErrorCodes.PseudoMustStartWithLetter;
    }

    /// <summary>Validates an already NFKC-normalized password; length is counted in Unicode code points.</summary>
    public static string? ValidatePassword(string normalizedPassword)
    {
        if (normalizedPassword.Length == 0)
        {
            return ErrorCodes.PasswordRequired;
        }

        int codePoints = normalizedPassword.EnumerateRunes().Count();
        if (codePoints < PasswordMinLength)
        {
            return ErrorCodes.PasswordTooShort;
        }

        return codePoints > PasswordMaxLength ? ErrorCodes.PasswordTooLong : null;
    }

    /// <summary>Machine-readable error codes of the contract (FieldError.code).</summary>
    public static class ErrorCodes
    {
        public const string BodyInvalid = "body_invalid";
        public const string EmailRequired = "email_required";
        public const string EmailInvalid = "email_invalid";
        public const string PseudoRequired = "pseudo_required";
        public const string PseudoTooShort = "pseudo_too_short";
        public const string PseudoTooLong = "pseudo_too_long";
        public const string PseudoInvalidCharacters = "pseudo_invalid_characters";
        public const string PseudoMustStartWithLetter = "pseudo_must_start_with_letter";
        public const string PseudoTaken = "pseudo_taken";
        public const string PasswordRequired = "password_required";
        public const string PasswordTooShort = "password_too_short";
        public const string PasswordTooLong = "password_too_long";
    }
}
