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
    public void ProductionCost_IsTheOwaspMinimum_AndReallyCostsTime()
    {
        Argon2idPasswordHasher production = new(Argon2idOptions.Default);
        production.Hash("warm-up password"); // loads libsodium

        Stopwatch watch = Stopwatch.StartNew();
        string hash = production.Hash("correct horse battery staple");
        watch.Stop();

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash, StringComparison.Ordinal);
        // 19 MiB filled twice cannot take under 5 ms; this would catch a memory size read in bytes instead of KiB.
        Assert.True(watch.Elapsed > TimeSpan.FromMilliseconds(5), $"Hashing took only {watch.Elapsed.TotalMilliseconds} ms.");
        TestContext.Current.SendDiagnosticMessage($"Argon2id (19 MiB, t=2, p=1): {watch.Elapsed.TotalMilliseconds:F1} ms");
    }
}
