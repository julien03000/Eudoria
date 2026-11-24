using System.Diagnostics;
using Eudoria.Core.Identity.Passwords;

namespace Eudoria.Core.Identity.Tests;

public sealed class Argon2idPasswordHasherTests
{
    // Low cost for speed; the production cost is checked separately below.
    private static readonly Argon2idOptions FastOptions = new(MemoryKib: 1024, Passes: 1);
    private readonly Argon2idPasswordHasher _hasher = new(FastOptions);

    [Fact]
    public void Verify_AcceptsTheRightPassword_AndRejectsAWrongOne()
    {
        string hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
        Assert.False(_hasher.Verify("correct horse battery stapl", hash));
        Assert.False(_hasher.Verify("Correct horse battery staple", hash));
    }

    [Fact]
    public void Hash_UsesTheStandardPhcFormat_WithItsParameters()
    {
        string hash = _hasher.Hash("correct horse battery staple");

        Assert.Matches(@"^\$argon2id\$v=19\$m=1024,t=1,p=1\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$", hash);
    }

    [Fact]
    public void Hash_UsesARandomSalt_SoTheSamePasswordNeverGivesTheSameHash()
    {
        Assert.NotEqual(_hasher.Hash("correct horse battery staple"), _hasher.Hash("correct horse battery staple"));
    }

    [Fact]
    public void Verify_UsesTheParametersStoredInTheHash_SoTheCostCanBeRaisedLater()
    {
        string oldHash = _hasher.Hash("correct horse battery staple");
        Argon2idPasswordHasher stronger = new(new Argon2idOptions(MemoryKib: 2048, Passes: 2));

        Assert.True(stronger.Verify("correct horse battery staple", oldHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("$argon2i$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=4$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=-1,t=1,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$not*base64$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")] // salt too short
    public void Verify_ReturnsFalse_ForAMalformedHash(string hash) =>
        Assert.False(_hasher.Verify("correct horse battery staple", hash));

    [Fact]
    public void NeedsRehash_IsTrue_OnlyForAValidHashMadeWithAnotherCost()
    {
        Argon2idPasswordHasher current = new(new Argon2idOptions(MemoryKib: 2048, Passes: 2));

        Assert.False(current.NeedsRehash(current.Hash("correct horse battery staple")));
        Assert.True(current.NeedsRehash(_hasher.Hash("correct horse battery staple"))); // older, cheaper cost
        Assert.True(current.NeedsRehash(new Argon2idPasswordHasher(new Argon2idOptions(2048, 3)).Hash("x"))); // other passes
        Assert.False(current.NeedsRehash("plain-text-password")); // not a hash: nothing to upgrade
        Assert.False(current.NeedsRehash(""));
    }

    [Fact]
    public void ProductionCost_IsTheChosenSetting_AndReallyCostsTime()
    {
        Argon2idPasswordHasher production = new(Argon2idOptions.Default);
        string hash = production.Hash("warm-up password"); // loads libsodium

        double[] timings = [.. Enumerable.Range(0, 5).Select(_ =>
        {
            Stopwatch watch = Stopwatch.StartNew();
            production.Hash("correct horse battery staple");
            return watch.Elapsed.TotalMilliseconds;
        }).Order()];
        double median = timings[2];

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", hash, StringComparison.Ordinal);
        // 64 MiB filled 3 times cannot take under 5 ms; this would catch a memory size read in bytes instead of KiB.
        Assert.True(median > 5, $"Hashing took only {median} ms.");
        TestContext.Current.SendDiagnosticMessage(
            $"Argon2id (64 MiB, t=3, p=1): median {median:F1} ms, min {timings[0]:F1} ms, max {timings[4]:F1} ms");
    }
}
