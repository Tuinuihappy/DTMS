using System.Diagnostics;
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
            limiter.OnRejected = (context, _) => Reject(context, options);
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

    /// <summary>
    /// What a refused caller is told. Without this a 429 goes out empty: no
    /// reason, and no <c>Retry-After</c>, so a polling client keeps asking at
    /// the same rate and a user sees "Upstream error 429".
    /// </summary>
    private static ValueTask Reject(OnRejectedContext context, RateLimitOptions options)
    {
        var http = context.HttpContext;
        var key = RateLimitPartitioner.For(http);
        var services = http.RequestServices;

        // The limiter knows when the window frees a permit; fall back to the
        // class's own window so the header is always present, whatever limiter
        // a future class uses.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var fromLease)
            ? fromLease
            : options.LimitFor(key.Class)?.Window ?? options.LegacyWindow;
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = seconds.ToString();

        services.GetRequiredService<RateLimitMetrics>().Rejected(key.Class);

        var tracker = services.GetRequiredService<WindowUsageTracker>();
        if (tracker.ShouldLogRejection(key))
        {
            // Metrics say a class is being refused; this says which caller and
            // which endpoint, which is what a quota gets fixed from.
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RateLimitingSetup))
                .LogWarning(
                    "Rate limit reached by {Class} {Caller} on {Method} {Path}; refusing for {RetryAfterSeconds}s",
                    key.Class, key.Key, http.Request.Method, http.Request.Path, seconds);
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        return new ValueTask(http.Response.WriteAsJsonAsync(new
        {
            status = StatusCodes.Status429TooManyRequests,
            title = "Too Many Requests",
            detail = $"You have reached the request limit. Try again in {seconds} second{(seconds == 1 ? "" : "s")}.",
            instance = http.Request.Path.Value,
            traceId,
            retryAfterSeconds = seconds,
            // Same content type ExceptionHandlingMiddleware uses, so one
            // client-side reader handles every modeled failure.
        }, options: null, contentType: "application/problem+json"));
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
