namespace Eudoria.Core.Identity.Accounts;

/// <summary>An EUDORIA account: one per person, shared by every world.</summary>
internal sealed class Account
{
    /// <summary>Stable internal identifier (UUID v7). The only key other systems may store.</summary>
    public Guid Id { get; init; }

    /// <summary>Normalized e-mail (trimmed, lower-cased). Unique. Personal data: never logged.</summary>
    public required string Email { get; init; }

    /// <summary>Public pseudo, as typed by its owner (e.g. "Ana_42").</summary>
    public required string Pseudo { get; init; }

    /// <summary>Lower-cased pseudo, unique: "Ana_42" and "ana_42" cannot coexist.</summary>
    public required string PseudoNormalized { get; init; }

    /// <summary>Argon2id hash in PHC string format. Never the password itself.</summary>
    public required string PasswordHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
