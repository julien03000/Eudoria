using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Eudoria.Core.Identity.Throttling;

/// <summary>
/// Counts failed password checks and blocks further attempts for a while (plan 1.4, section 3).
/// Two counters per attempt:
/// <list type="bullet">
/// <item>identifier + client address: <see cref="ThrottlingOptions.FreeFailures"/> free failures, then a
/// block that doubles at each new failure (1, 2, 4… up to 15 min); reset by a success;</item>
/// <item>identifier alone (every address): <see cref="ThrottlingOptions.IdentifierMaxFailures"/> failures
/// within a window → block (protects against attacks spread over many addresses).</item>
/// </list>
/// An attempt is reserved before the password is checked, so simultaneous attempts cannot exceed the
/// limits. The identifier is any string (a normalized e-mail, known or not, or an account id): an
/// unknown e-mail is counted exactly like a known one. Keys are HMAC-SHA-256 with a random key drawn at
/// startup and never stored: no e-mail or address is kept in clear. In memory, bounded (the least
/// recently used counters are forgotten first); reset when the core restarts (owner decision, point 2).
/// </summary>
internal sealed class LoginThrottle(IOptions<ThrottlingOptions> options, TimeProvider time)
{
    private readonly byte[] _keySecret = RandomNumberGenerator.GetBytes(32);
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<Counter>> _counters = new(StringComparer.Ordinal);
    private readonly LinkedList<Counter> _recency = new(); // least recently used first

    /// <summary>Number of counters currently kept (for tests).</summary>
    public int TrackedKeys
    {
        get
        {
            lock (_lock)
            {
                return _counters.Count;
            }
        }
    }

    /// <summary>
    /// Reserves one attempt for <paramref name="identifier"/> from <paramref name="clientKey"/>
    /// (<see cref="ClientAddress.Key"/>). <paramref name="scope"/> separates unrelated counters
    /// ("login", "password-change"). The caller must complete the returned attempt.
    /// </summary>
    public ThrottledAttempt TryBegin(string scope, string identifier, string clientKey)
    {
        string identifierKey = Hash($"{scope}\n{identifier}");
        string pairKey = Hash($"{scope}\n{identifier}\n{clientKey}");
        ThrottlingOptions settings = options.Value;
        DateTimeOffset now = time.GetUtcNow();

        lock (_lock)
        {
            Counter pair = Touch(pairKey, now, settings);
            Counter alone = Touch(identifierKey, now, settings);

            DateTimeOffset blockedUntil = Max(pair.BlockedUntil, alone.BlockedUntil);
            if (blockedUntil > now)
            {
                return ThrottledAttempt.Refused(blockedUntil - now);
            }

            // Attempts already being checked count as failures in advance.
            int pairRoom = Math.Max(1, settings.FreeFailures - pair.Failures);
            int aloneRoom = settings.IdentifierMaxFailures - alone.WindowFailures;
            if (pair.InFlight >= pairRoom || alone.InFlight >= aloneRoom)
            {
                return ThrottledAttempt.Refused(TimeSpan.FromSeconds(1));
            }

            pair.InFlight++;
            alone.InFlight++;
            return new ThrottledAttempt(this, pairKey, identifierKey);
        }
    }

    internal void Complete(string pairKey, string identifierKey, AttemptOutcome outcome)
    {
        ThrottlingOptions settings = options.Value;
        DateTimeOffset now = time.GetUtcNow();

        lock (_lock)
        {
            Counter pair = Touch(pairKey, now, settings);
            Counter alone = Touch(identifierKey, now, settings);
            pair.InFlight = Math.Max(0, pair.InFlight - 1);
            alone.InFlight = Math.Max(0, alone.InFlight - 1);

            switch (outcome)
            {
                case AttemptOutcome.Succeeded:
                    pair.Failures = 0;
                    break;

                case AttemptOutcome.Failed:
                    pair.Failures++;
                    pair.LastFailure = now;
                    if (pair.Failures >= settings.FreeFailures)
                    {
                        int doublings = Math.Min(pair.Failures - settings.FreeFailures, 20);
                        TimeSpan block = settings.FirstBlock * Math.Pow(2, doublings);
                        pair.BlockedUntil = now + (block < settings.MaxBlock ? block : settings.MaxBlock);
                    }

                    alone.WindowFailures++;
                    if (alone.WindowFailures >= settings.IdentifierMaxFailures)
                    {
                        alone.BlockedUntil = now + settings.IdentifierBlock;
                        alone.WindowStart = now;
                        alone.WindowFailures = 0;
                    }

                    break;

                default: // abandoned: neither a success nor a failure (e.g. invalid new password)
                    break;
            }
        }
    }

    /// <summary>Finds or creates a counter, marks it as recently used, forgets expired history and old counters.</summary>
    private Counter Touch(string key, DateTimeOffset now, ThrottlingOptions settings)
    {
        if (_counters.TryGetValue(key, out LinkedListNode<Counter>? node))
        {
            _recency.Remove(node);
            _recency.AddLast(node);
        }
        else
        {
            node = _recency.AddLast(new Counter(key) { WindowStart = now });
            _counters[key] = node;
            while (_counters.Count > settings.MaxTrackedKeys && _recency.First is { } oldest && oldest != node)
            {
                _recency.RemoveFirst();
                _counters.Remove(oldest.Value.Key);
            }
        }

        Counter counter = node.Value;
        if (counter.Failures > 0 && counter.BlockedUntil <= now && now - counter.LastFailure >= settings.FailureMemory)
        {
            counter.Failures = 0;
        }

        if (now - counter.WindowStart >= settings.IdentifierWindow)
        {
            counter.WindowStart = now;
            counter.WindowFailures = 0;
        }

        return counter;
    }

    private string Hash(string value) =>
        Convert.ToBase64String(HMACSHA256.HashData(_keySecret, Encoding.UTF8.GetBytes(value)));

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private sealed class Counter(string key)
    {
        public string Key { get; } = key;

        public int InFlight { get; set; }

        public int Failures { get; set; }

        public DateTimeOffset LastFailure { get; set; }

        public DateTimeOffset BlockedUntil { get; set; }

        public DateTimeOffset WindowStart { get; set; }

        public int WindowFailures { get; set; }
    }
}

internal enum AttemptOutcome
{
    Abandoned,
    Succeeded,
    Failed,
}

/// <summary>
/// One reserved attempt, or a refusal (<see cref="Allowed"/> false, with <see cref="RetryAfter"/>).
/// Disposing an attempt that was not completed releases it without counting anything.
/// </summary>
internal sealed class ThrottledAttempt : IDisposable
{
    private readonly LoginThrottle? _throttle;
    private readonly string _pairKey = string.Empty;
    private readonly string _identifierKey = string.Empty;
    private bool _completed;

    internal ThrottledAttempt(LoginThrottle throttle, string pairKey, string identifierKey)
    {
        _throttle = throttle;
        _pairKey = pairKey;
        _identifierKey = identifierKey;
    }

    private ThrottledAttempt(TimeSpan retryAfter)
    {
        RetryAfter = retryAfter;
        _completed = true;
    }

    public bool Allowed => _throttle is not null;

    /// <summary>For a refusal: how long to wait.</summary>
    public TimeSpan RetryAfter { get; }

    public static ThrottledAttempt Refused(TimeSpan retryAfter) => new(retryAfter);

    public void Succeeded() => Complete(AttemptOutcome.Succeeded);

    public void Failed() => Complete(AttemptOutcome.Failed);

    public void Dispose() => Complete(AttemptOutcome.Abandoned);

    private void Complete(AttemptOutcome outcome)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _throttle!.Complete(_pairKey, _identifierKey, outcome);
    }
}
