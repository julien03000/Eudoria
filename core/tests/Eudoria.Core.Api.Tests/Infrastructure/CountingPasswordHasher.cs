using Eudoria.Core.Identity.Passwords;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>Real Argon2id hasher that also counts how many hashes and verifications were computed.</summary>
internal sealed class CountingPasswordHasher : IPasswordHasher
{
    private readonly Argon2idPasswordHasher _inner = new(Argon2idOptions.Default);
    private int _hashCount;
    private int _verifyCount;

    public int HashCount => _hashCount;

    public int VerifyCount => _verifyCount;

    public string Hash(string password)
    {
        Interlocked.Increment(ref _hashCount);
        return _inner.Hash(password);
    }

    public bool Verify(string password, string hash)
    {
        Interlocked.Increment(ref _verifyCount);
        return _inner.Verify(password, hash);
    }

    public bool NeedsRehash(string hash) => _inner.NeedsRehash(hash);
}
