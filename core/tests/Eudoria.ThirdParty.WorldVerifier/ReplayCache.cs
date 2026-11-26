namespace Eudoria.ThirdParty.WorldVerifier;

public enum ReplayOutcome
{
    /// <summary>First use: remembered until it can no longer be accepted anyway.</summary>
    FirstUse,

    /// <summary>Already accepted once: refuse.</summary>
    Replayed,

    /// <summary>No room left: refuse (better than letting a replay through).</summary>
    Full,
}

/// <summary>
/// Memory of the accepted ticket identifiers ("jti"), each kept until its ticket expires (exp + clock
/// tolerance), then forgotten — a ticket lives 75 s at most, so the memory stays tiny. Bounded: when
/// full, new tickets are refused. Not shared between instances and lost on restart (known limit, step 6).
/// </summary>
public sealed class ReplayCache(int capacity = ReplayCache.DefaultCapacity)
{
    public const int DefaultCapacity = 100_000;

    private readonly Dictionary<string, long> _forgetAfter = new(StringComparer.Ordinal);
    private readonly PriorityQueue<string, long> _byExpiry = new();
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _forgetAfter.Count;
            }
        }
    }

    /// <param name="jti">The ticket identifier.</param>
    /// <param name="forgetAfterUnixSeconds">Last second at which the ticket can still be accepted (exp + tolerance).</param>
    /// <param name="nowUnixSeconds">Current time.</param>
    public ReplayOutcome TryRemember(string jti, long forgetAfterUnixSeconds, long nowUnixSeconds)
    {
        lock (_lock)
        {
            // Forget what can no longer be accepted anyway (expired tickets are refused before coming here).
            while (_byExpiry.TryPeek(out string? oldest, out long forgetAfter) && forgetAfter < nowUnixSeconds)
            {
                _byExpiry.Dequeue();
                _forgetAfter.Remove(oldest);
            }

            if (_forgetAfter.ContainsKey(jti))
            {
                return ReplayOutcome.Replayed;
            }

            if (_forgetAfter.Count >= capacity)
            {
                return ReplayOutcome.Full;
            }

            _forgetAfter.Add(jti, forgetAfterUnixSeconds);
            _byExpiry.Enqueue(jti, forgetAfterUnixSeconds);
            return ReplayOutcome.FirstUse;
        }
    }
}
