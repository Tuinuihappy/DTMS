using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DTMS.Api.Infrastructure.RateLimiting;

/// <summary>
/// Registers and places the rate limiter. See <see cref="RateLimitOptions"/>
/// for the two modes and <see cref="RateLimitPartitioner"/> for who spends
/// which quota.
/// </summary>
public static class RateLimitingSetup
{
    public static IServiceCollection AddDtmsRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(RateLimitOptions.SectionName);
        services.Configure<RateLimitOptions>(section);

        // Read once for the limiter's partition factories, which run outside
        // DI. The same section feeds IOptions for everything else.
        var options = section.Get<RateLimitOptions>() ?? new RateLimitOptions();
        options.Validate();

        services.AddSingleton<RateLimitMetrics>();
        services.AddSingleton<WindowUsageTracker>();
        services.AddHostedService(sp => sp.GetRequiredService<WindowUsageTracker>());

        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var key = RateLimitPartitioner.For(ctx);
                return options.Enforce ? Enforced(key, options) : Legacy(key, ctx, options);
            });
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    /// <summary>
    /// Classifies and counts the request, then applies the limit. Place after
    /// authentication and after the <c>/api/v1/source</c> system-client branch,
    /// and before authorization: see <see cref="RateLimitPartitioner"/>.
    /// </summary>
    public static IApplicationBuilder UseDtmsRateLimiting(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            // Resolved and counted once here, not in the limiter's partition
            // callback: the limiter calls that again for a request it queues, and
            // a queued request would then be counted twice.
            var key = RateLimitPartitioner.Resolve(ctx);
            ctx.Items[RateLimitPartitioner.ItemKey] = key;
            ctx.RequestServices.GetRequiredService<WindowUsageTracker>().Observe(key);
            await next(ctx);
        });
        return app.UseRateLimiter();
    }

    private static RateLimitPartition<string> Enforced(RateLimitPartitionKey key, RateLimitOptions options)
    {
        var limit = options.LimitFor(key.Class);
        if (limit is null)
            return RateLimitPartition.GetNoLimiter($"unlimited:{key.Class}");

        var (permitLimit, window) = limit.Value;
        return RateLimitPartition.GetSlidingWindowLimiter($"{key.Class}:{key.Key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            SegmentsPerWindow = options.SegmentsPerWindow,
            // Refuse at once rather than queue. A queued request waits past the
            // Next proxy's 20 s timeout, becomes a 504, and the API still does
            // the work for nobody.
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>
    /// The original single per-IP limiter, kept in force while the per-class
    /// quotas are only being measured, so measuring changes nothing a user
    /// sees. One deliberate exception: vendor webhooks are exempt already,
    /// because a rejected frame is lost for good and there is no reason to keep
    /// risking that through the measurement.
    /// </summary>
    private static RateLimitPartition<string> Legacy(
        RateLimitPartitionKey key, HttpContext ctx, RateLimitOptions options)
    {
        if (key.Class is TrafficClass.Infra or TrafficClass.Webhook)
            return RateLimitPartition.GetNoLimiter("bypass");

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (key.Class == TrafficClass.AttachmentImage)
            return RateLimitPartition.GetFixedWindowLimiter("attachment-images:" + ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.LegacyPermitLimit * 20,
                Window = options.LegacyWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 100,
            });

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.LegacyPermitLimit,
            Window = options.LegacyWindow,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = options.LegacyQueueLimit,
        });
    }
}
