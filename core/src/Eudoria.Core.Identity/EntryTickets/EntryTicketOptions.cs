namespace Eudoria.Core.Identity.EntryTickets;

/// <summary>
/// World entry ticket settings (configuration section <c>Identity:EntryTickets</c>, ADR-0007). Everything
/// but the signing key lives in appsettings.json; the key is a secret, given by the environment variable
/// <c>Identity__EntryTickets__SigningKey</c> (tools/dev/run-core.ps1 reads it from .env).
/// </summary>
internal sealed class EntryTicketOptions
{
    public const string SectionName = "Identity:EntryTickets";

    /// <summary>Prefix of a ticket's "aud" claim, followed by the world identifier.</summary>
    public const string AudiencePrefix = "eudoria-world:";

    /// <summary>Upper bound accepted for <see cref="Lifetime"/>: a ticket must stay short-lived.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Validity of a ticket after it is issued (owner decision: 60 s).</summary>
    public TimeSpan Lifetime { get; set; }

    /// <summary>The worlds the core issues tickets for, until the world registry exists (step 6).</summary>
    public string[] KnownWorlds { get; set; } = [];

    /// <summary>ES256 private key (ECDSA P-256, PKCS#8, Base64), distinct from the access-token key. Secret: never logged.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Whole seconds between 1 s and <see cref="MaxLifetime"/>; at least one world, each well-formed, no duplicate.</summary>
    public bool IsValid() =>
        Lifetime >= TimeSpan.FromSeconds(1) && Lifetime <= MaxLifetime && Lifetime.Ticks % TimeSpan.TicksPerSecond == 0
        && KnownWorlds.Length > 0 && KnownWorlds.All(WorldIds.IsValid)
        && KnownWorlds.Distinct(StringComparer.Ordinal).Count() == KnownWorlds.Length;
}
