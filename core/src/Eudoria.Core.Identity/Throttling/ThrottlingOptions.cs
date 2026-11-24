namespace Eudoria.Core.Identity.Throttling;

/// <summary>
/// Limits against brute force and overload (section "Identity:Throttling" of the configuration).
/// Defaults: owner decision of plan 1.4, section 11, point 1.
/// </summary>
internal sealed class ThrottlingOptions
{
    public const string SectionName = "Identity:Throttling";

    /// <summary>Failed attempts allowed per identifier (e-mail or account) and client address before blocking.</summary>
    public int FreeFailures { get; set; } = 5;

    /// <summary>First block after the free failures; doubled at each new failure, up to <see cref="MaxBlock"/>.</summary>
    public TimeSpan FirstBlock { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaxBlock { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Failures (identifier + client address) are forgotten after this long without a new one.</summary>
    public TimeSpan FailureMemory { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Failed attempts per identifier alone, from every address, within <see cref="IdentifierWindow"/>.</summary>
    public int IdentifierMaxFailures { get; set; } = 20;

    public TimeSpan IdentifierWindow { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan IdentifierBlock { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Most counters kept in memory; the least recently used are forgotten first.</summary>
    public int MaxTrackedKeys { get; set; } = 100_000;

    /// <summary>Requests per client address (IPv4 address or IPv6 /64), per route.</summary>
    public RateLimit Login { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) };

    public RateLimit Registration { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromHours(1) };

    public RateLimit Refresh { get; set; } = new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Password change and "revoke all sessions".</summary>
    public RateLimit Account { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Argon2id hashes computed at the same time (64 MiB each).</summary>
    public int MaxConcurrentHashes { get; set; } = 4;

    /// <summary>How long a request waits for a hashing slot before getting 503.</summary>
    public TimeSpan HashQueueTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsValid() =>
        FreeFailures >= 1 && FirstBlock > TimeSpan.Zero && MaxBlock >= FirstBlock && FailureMemory > TimeSpan.Zero
        && IdentifierMaxFailures >= 1 && IdentifierWindow > TimeSpan.Zero && IdentifierBlock > TimeSpan.Zero
        && MaxTrackedKeys >= 2 && MaxConcurrentHashes >= 1 && HashQueueTimeout >= TimeSpan.Zero
        && Login.IsValid() && Registration.IsValid() && Refresh.IsValid() && Account.IsValid();
}

/// <summary>A sliding window: at most <see cref="PermitLimit"/> requests per <see cref="Window"/>.</summary>
internal sealed class RateLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }

    public bool IsValid() => PermitLimit >= 1 && Window >= TimeSpan.FromSeconds(1);
}
