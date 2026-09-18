namespace DTMS.Api.Infrastructure.RateLimiting;

/// <summary>
/// Rate-limit configuration, bound from the <c>RateLimit</c> section
/// (env: <c>RateLimit__Enforce</c>, <c>RateLimit__User__PermitLimit</c>, …).
///
/// <para><b>Two modes.</b> With <see cref="Enforce"/> off — the default — the
/// original per-IP limit stays in force exactly as before, while every request
/// is also classified by caller identity and counted against the per-class
/// quotas below without rejecting anything. That measurement is what the real
/// quotas are set from. With it on, the per-class quotas are enforced.</para>
///
/// <para><b>The flat keys belong to the original limiter only.</b>
/// <c>PermitLimit</c> / <c>WindowSeconds</c> / <c>QueueLimit</c> still size it
/// while it is in force, which is what <c>.env.test</c>'s
/// <c>RateLimit__PermitLimit=100000</c> load-test override does. They
/// deliberately do <b>not</b> touch the per-class quotas: docker-compose sets
/// <c>PermitLimit</c> to 100 by default, and letting that reach the classes
/// would silently cap every caller at 100 a minute the day enforcement is
/// turned on. A load test under enforcement raises the class keys instead.</para>
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public bool Enforce { get; set; }

    // Starting points, sized from the frontend's own polling: a user with three
    // tabs open runs about 200-250 requests a minute. Confirm against the
    // measured partition peak before enforcing.
    public ClassLimit User { get; set; } = new() { PermitLimit = 600 };
    public ClassLimit System { get; set; } = new() { PermitLimit = 1200 };
    public ClassLimit AttachmentImage { get; set; } = new() { PermitLimit = 3000 };

    // Shared: requests without a token still arrive from the Next server's one
    // IP. Generous enough that sessions expiring together still reach their 401
    // and the login redirect, rather than a 429.
    public ClassLimit Anonymous { get; set; } = new() { PermitLimit = 300 };

    /// <summary>Sliding-window resolution, used both for measuring and enforcing
    /// so a measured peak means the same thing as the limit it sizes.</summary>
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>Share of a quota at which a partition counts as near its limit.</summary>
    public double NearLimitRatio { get; set; } = 0.8;

    public int? PermitLimit { get; set; }
    public int? WindowSeconds { get; set; }
    public int? QueueLimit { get; set; }

    // The original single limiter's values, still used while not enforcing.
    internal int LegacyPermitLimit => PermitLimit ?? 100;
    internal TimeSpan LegacyWindow => TimeSpan.FromSeconds(WindowSeconds ?? 60);
    internal int LegacyQueueLimit => QueueLimit ?? 5;

    /// <summary>The quota for a class. Null for classes that are never limited.</summary>
    public (int PermitLimit, TimeSpan Window)? LimitFor(TrafficClass trafficClass)
    {
        var limit = trafficClass switch
        {
            TrafficClass.User => User,
            TrafficClass.System => System,
            TrafficClass.AttachmentImage => AttachmentImage,
            TrafficClass.Anonymous => Anonymous,
            _ => null,
        };

        return limit is null
            ? null
            : (limit.PermitLimit, TimeSpan.FromSeconds(limit.WindowSeconds));
    }

    /// <summary>Fails startup on values the limiter would reject at request time,
    /// where the error would surface as a 500 on every call.</summary>
    public void Validate()
    {
        foreach (var c in new[] { TrafficClass.User, TrafficClass.System, TrafficClass.AttachmentImage, TrafficClass.Anonymous })
        {
            var (permit, window) = LimitFor(c)!.Value;
            if (permit <= 0)
                throw new InvalidOperationException($"RateLimit: {c} PermitLimit must be positive, got {permit}.");
            if (window <= TimeSpan.Zero)
                throw new InvalidOperationException($"RateLimit: {c} WindowSeconds must be positive, got {window.TotalSeconds}.");
        }
        if (SegmentsPerWindow <= 0)
            throw new InvalidOperationException($"RateLimit: SegmentsPerWindow must be positive, got {SegmentsPerWindow}.");
        if (NearLimitRatio is <= 0 or > 1)
            throw new InvalidOperationException($"RateLimit: NearLimitRatio must be in (0, 1], got {NearLimitRatio}.");
    }
}

public sealed class ClassLimit
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; } = 60;
}
