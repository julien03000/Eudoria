using Eudoria.Core.Identity.Throttling;
using Microsoft.Extensions.Options;

namespace Eudoria.Core.Identity.Passwords;

/// <summary>
/// Bounds the number of Argon2id computations running at the same time (64 MiB each), so a burst of
/// logins or registrations cannot exhaust the memory. A request waits for a slot at most
/// <see cref="ThrottlingOptions.HashQueueTimeout"/>, then gets 503 (<see cref="PasswordHashingBusyException"/>).
/// </summary>
internal sealed class PasswordHashingGate(IOptions<ThrottlingOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentHashes, options.Value.MaxConcurrentHashes);

    /// <summary>Waits for a hashing slot; dispose the result to give it back.</summary>
    public async Task<Slot> EnterAsync(CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(options.Value.HashQueueTimeout, cancellationToken))
        {
            throw new PasswordHashingBusyException();
        }

        return new Slot(_slots);
    }

    public void Dispose() => _slots.Dispose();

    /// <summary>One hashing slot, given back on dispose.</summary>
    internal sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}

/// <summary>No hashing slot became free in time: answered 503 server_busy.</summary>
internal sealed class PasswordHashingBusyException : Exception
{
    public PasswordHashingBusyException()
        : base("Too many password hashes in progress.")
    {
    }

    public PasswordHashingBusyException(string message)
        : base(message)
    {
    }

    public PasswordHashingBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
