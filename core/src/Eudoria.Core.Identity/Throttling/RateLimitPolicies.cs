using System.Threading.RateLimiting;
using Eudoria.Core.Identity.Http;
using Eudoria.Core.Identity.Passwords;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Eudoria.Core.Identity.Throttling;

/// <summary>
/// Request rate limits per client address (ASP.NET Core's built-in rate limiter), one sliding
/// window per route family. Rejections get the contract's 429 with Retry-After.
/// </summary>
internal static class RateLimitPolicies
{
    public const string Login = "identity-login";
    public const string Registration = "identity-registration";
    public const string Refresh = "identity-refresh";
    public const string Account = "identity-account";

    private const int SegmentsPerWindow = 6;

    public static void Configure(RateLimiterOptions limiter, ThrottlingOptions settings)
    {
        Add(limiter, Login, settings.Login);
        Add(limiter, Registration, settings.Registration);
        Add(limiter, Refresh, settings.Refresh);
        Add(limiter, Account, settings.Account);

        limiter.OnRejected = async (context, cancellationToken) =>
        {
            TimeSpan retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan fromLease)
                ? fromLease
                : SegmentOf(PolicyOf(context.HttpContext) switch
                {
                    Registration => settings.Registration,
                    Refresh => settings.Refresh,
                    Account => settings.Account,
                    _ => settings.Login,
                });
            await ProblemResults.TooManyRequests(context.HttpContext, retryAfter).ExecuteAsync(context.HttpContext);
        };
    }

    /// <summary>Answers 503 server_busy when no password hashing slot became free in time.</summary>
    public static TBuilder WithHashingBusyAnswer<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (PasswordHashingBusyException)
            {
                return ProblemResults.ServerBusy(context.HttpContext);
            }
        });

    private static void Add(RateLimiterOptions limiter, string policy, RateLimit limit) =>
        limiter.AddPolicy(policy, context => RateLimitPartition.GetSlidingWindowLimiter(
            ClientAddress.Key(context.Connection.RemoteIpAddress),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
            }));

    private static string? PolicyOf(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static TimeSpan SegmentOf(RateLimit limit) => limit.Window / SegmentsPerWindow;
}
