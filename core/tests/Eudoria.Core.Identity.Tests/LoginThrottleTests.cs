using System.Net;
using Eudoria.Core.Identity.Throttling;
using Microsoft.Extensions.Options;

namespace Eudoria.Core.Identity.Tests;

public sealed class LoginThrottleTests
{
    private const string Scope = "login";
    private const string Email = "ana@example.com";
    private const string Address = "203.0.113.7";

    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void FiveFailures_ThenBlocked_WithProgressiveDurations_UpTo15Minutes()
    {
        LoginThrottle throttle = Create();
        FailTimes(throttle, 5);

        AssertRefused(throttle, TimeSpan.FromMinutes(1));

        TimeSpan[] expectedBlocks = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)];
        TimeSpan previous = TimeSpan.FromMinutes(1);
        foreach (TimeSpan expected in expectedBlocks)
        {
            _time.Advance(previous); // the block ends: one more try is allowed
            FailTimes(throttle, 1);
            AssertRefused(throttle, expected);
            previous = expected;
        }
    }

    [Fact]
    public void ASuccess_ResetsTheFailures()
    {
        LoginThrottle throttle = Create();
        FailTimes(throttle, 4);

        using (ThrottledAttempt attempt = throttle.TryBegin(Scope, Email, Address))
        {
            attempt.Succeeded();
        }

        FailTimes(throttle, 4);
        using ThrottledAttempt next = throttle.TryBegin(Scope, Email, Address);
        Assert.True(next.Allowed);
    }

    [Fact]
    public void Failures_AreForgottenAfter24HoursWithoutANewOne()
    {
        LoginThrottle throttle = Create();
        FailTimes(throttle, 4);
        _time.Advance(TimeSpan.FromHours(24));

        FailTimes(throttle, 4);

        using ThrottledAttempt attempt = throttle.TryBegin(Scope, Email, Address);
        Assert.True(attempt.Allowed);
    }

    [Fact]
    public void AnotherAddress_IsNotBlocked_UntilTheIdentifierLimit()
    {
        LoginThrottle throttle = Create(options => options.IdentifierMaxFailures = 8);
        FailTimes(throttle, 5);
        AssertRefused(throttle, TimeSpan.FromMinutes(1));

        FailTimes(throttle, 3, "198.51.100.1"); // 8 failures for this e-mail in the window

        foreach (string address in new[] { "198.51.100.2", "192.0.2.99" })
        {
            using ThrottledAttempt attempt = throttle.TryBegin(Scope, Email, address);
            Assert.False(attempt.Allowed);
            Assert.Equal(TimeSpan.FromMinutes(15), attempt.RetryAfter);
        }

        using ThrottledAttempt otherEmail = throttle.TryBegin(Scope, "bob@example.com", "192.0.2.99");
        Assert.True(otherEmail.Allowed);
    }

    [Fact]
    public void IdentifierWindow_Ends_FailuresOfAnEndedWindowDoNotAddUp()
    {
        LoginThrottle throttle = Create(options => options.IdentifierMaxFailures = 4);
        FailTimes(throttle, 3, "198.51.100.1");
        _time.Advance(TimeSpan.FromMinutes(15));

        FailTimes(throttle, 3, "198.51.100.2");

        using ThrottledAttempt attempt = throttle.TryBegin(Scope, Email, "198.51.100.3");
        Assert.True(attempt.Allowed);
    }

    [Fact]
    public void AttemptsInProgress_CountInAdvance()
    {
        LoginThrottle throttle = Create();
        List<ThrottledAttempt> inProgress = [.. Enumerable.Range(0, 5).Select(_ => throttle.TryBegin(Scope, Email, Address))];

        Assert.All(inProgress, attempt => Assert.True(attempt.Allowed));
        using (ThrottledAttempt sixth = throttle.TryBegin(Scope, Email, Address))
        {
            Assert.False(sixth.Allowed);
        }

        inProgress[0].Dispose(); // abandoned: frees its place without counting
        using ThrottledAttempt again = throttle.TryBegin(Scope, Email, Address);
        Assert.True(again.Allowed);
        inProgress.ForEach(attempt => attempt.Dispose());
    }

    [Fact]
    public void Scopes_AreSeparate()
    {
        LoginThrottle throttle = Create();
        FailTimes(throttle, 5);

        using ThrottledAttempt attempt = throttle.TryBegin("password-change", Email, Address);
        Assert.True(attempt.Allowed);
    }

    [Fact]
    public void TrackedKeys_AreBounded()
    {
        LoginThrottle throttle = Create(options => options.MaxTrackedKeys = 100);

        for (int i = 0; i < 1000; i++)
        {
            using ThrottledAttempt attempt = throttle.TryBegin(Scope, $"invented-{i}@example.com", Address);
            attempt.Failed();
        }

        Assert.InRange(throttle.TrackedKeys, 1, 100);
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")] // IPv4 mapped to IPv6
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    [InlineData("fe80::1%3", "fe80::/64")] // zone dropped
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    public void ClientAddress_IsTheIpv4Address_OrTheIpv6Slash64Prefix(string address, string expected) =>
        Assert.Equal(expected, ClientAddress.Key(IPAddress.Parse(address)));

    [Fact]
    public void ClientAddress_Unknown_SharesOneKey() =>
        Assert.Equal(ClientAddress.Unknown, ClientAddress.Key(null));

    private LoginThrottle Create(Action<ThrottlingOptions>? configure = null)
    {
        ThrottlingOptions options = new();
        configure?.Invoke(options);
        Assert.True(options.IsValid());
        return new LoginThrottle(Options.Create(options), _time);
    }

    private static void FailTimes(LoginThrottle throttle, int count, string address = Address)
    {
        for (int i = 0; i < count; i++)
        {
            using ThrottledAttempt attempt = throttle.TryBegin(Scope, Email, address);
            Assert.True(attempt.Allowed, $"attempt {i + 1} should be allowed");
            attempt.Failed();
        }
    }

    private static void AssertRefused(LoginThrottle throttle, TimeSpan retryAfter)
    {
        using ThrottledAttempt attempt = throttle.TryBegin(Scope, Email, Address);
        Assert.False(attempt.Allowed);
        Assert.Equal(retryAfter, attempt.RetryAfter);
    }

    /// <summary>A clock that only moves when told to.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
