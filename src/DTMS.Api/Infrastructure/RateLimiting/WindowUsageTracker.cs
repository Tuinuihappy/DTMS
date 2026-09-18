using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Infrastructure.RateLimiting;

/// <summary>
/// Counts each caller's requests in a sliding window with the same shape the
/// enforcing limiter uses, and turns those counts into metrics: requests per
/// class, callers over or near their quota, and the busiest caller's count.
///
/// <para>It exists so quotas can be sized from real traffic before anything is
/// refused, and it keeps running once they are enforced so the near-limit
/// signal stays available. Counting mirrors <c>SlidingWindowRateLimiter</c>
/// (same window, same segments) so a measured peak means what the limit
/// means.</para>
///
/// <para>Idle callers are swept periodically. The table is also capped, so a
/// flood of distinct anonymous addresses cannot grow it without bound; past
/// the cap new callers are simply not measured.</para>
/// </summary>
public sealed class WindowUsageTracker : BackgroundService
{
    internal const int MaxTrackedCallers = 50_000;

    private readonly ConcurrentDictionary<string, SlidingCount> _counts = new();
    private readonly ConcurrentDictionary<string, long> _lastLoggedWindow = new();
    private readonly RateLimitOptions _options;
    private readonly RateLimitMetrics _metrics;
    private readonly TimeProvider _time;
    private readonly ILogger<WindowUsageTracker> _logger;
    private int _capWarned;

    public WindowUsageTracker(
        IOptions<RateLimitOptions> options,
        RateLimitMetrics metrics,
        ILogger<WindowUsageTracker> logger,
        TimeProvider? time = null)
    {
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    internal int TrackedCallers => _counts.Count;

    /// <summary>Records one request. Returns the caller's count in the current
    /// window including this one, or null when the class is not measured
    /// against a quota.</summary>
    public int? Observe(RateLimitPartitionKey key)
    {
        if (key.Class == TrafficClass.Infra) return null;
        _metrics.Request(key.Class);

        var limit = _options.LimitFor(key.Class);
        if (limit is null) return null;
        var (permitLimit, window) = limit.Value;

        var id = $"{key.Class}:{key.Key}";
        if (!_counts.TryGetValue(id, out var counter))
        {
            if (_counts.Count >= MaxTrackedCallers)
            {
                if (Interlocked.Exchange(ref _capWarned, 1) == 0)
                    _logger.LogWarning(
                        "Rate-limit usage tracking is at its cap of {Cap} callers; new callers are not measured until idle ones are swept",
                        MaxTrackedCallers);
                return null;
            }
            counter = _counts.GetOrAdd(id, _ => new SlidingCount(_options.SegmentsPerWindow));
        }

        var segment = SegmentOf(window);
        var (before, after) = counter.Add(segment);

        var nearAt = (int)Math.Ceiling(permitLimit * _options.NearLimitRatio);
        if (before < nearAt && after >= nearAt) _metrics.NearLimit(key.Class);
        if (after > permitLimit && !_options.Enforce) _metrics.WouldReject(key.Class);
        _metrics.WindowCount(key.Class, after);

        return after;
    }

    /// <summary>True the first time a caller is refused in the current window.
    /// Rejections arrive in bursts — one line names who and where, a line per
    /// refused request would bury it.</summary>
    public bool ShouldLogRejection(RateLimitPartitionKey key)
    {
        var limit = _options.LimitFor(key.Class);
        if (limit is null) return true;

        var segment = SegmentOf(limit.Value.Window) / _options.SegmentsPerWindow;
        var id = $"{key.Class}:{key.Key}";
        var first = false;
        _lastLoggedWindow.AddOrUpdate(
            id,
            _ => { first = true; return segment; },
            (_, previous) =>
            {
                if (previous == segment) return previous;
                first = true;
                return segment;
            });
        return first;
    }

    private long SegmentOf(TimeSpan window)
    {
        var segmentMs = Math.Max(1, (long)window.TotalMilliseconds / _options.SegmentsPerWindow);
        return _time.GetUtcNow().ToUnixTimeMilliseconds() / segmentMs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The effective configuration, said once at startup: which mode is on
        // and what each caller may spend. Without this, reading a 429 in the
        // logs means guessing which of two limiters produced it.
        _logger.LogInformation(
            "Rate limiting: {Mode}. Per caller per minute — user {User}, system {System}, images {Images}, anonymous {Anonymous}. " +
            "Original per-IP limit {Legacy}/{LegacyWindow}s, queue {Queue}",
            _options.Enforce ? "enforcing per-caller quotas" : "measuring only, original per-IP limit in force",
            _options.User.PermitLimit, _options.System.PermitLimit,
            _options.AttachmentImage.PermitLimit, _options.Anonymous.PermitLimit,
            _options.LegacyPermitLimit, _options.LegacyWindow.TotalSeconds, _options.LegacyQueueLimit);

        // Sweep often enough to bound memory, rarely enough to cost nothing.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            Sweep();
    }

    /// <summary>Drops callers with nothing left in their window.</summary>
    internal void Sweep()
    {
        foreach (var (id, counter) in _counts)
        {
            var separator = id.IndexOf(':');
            if (!Enum.TryParse<TrafficClass>(id[..separator], out var c)) continue;
            var limit = _options.LimitFor(c);
            if (limit is null || counter.IsIdle(SegmentOf(limit.Value.Window)))
            {
                _counts.TryRemove(id, out _);
                _lastLoggedWindow.TryRemove(id, out _);
            }
        }
        if (_counts.Count < MaxTrackedCallers) Interlocked.Exchange(ref _capWarned, 0);
    }

    /// <summary>Requests per segment over the last N segments.</summary>
    internal sealed class SlidingCount
    {
        private readonly int[] _segments;
        private long _head = -1;
        private int _total;

        public SlidingCount(int segments) => _segments = new int[segments];

        public (int Before, int After) Add(long segment)
        {
            lock (_segments)
            {
                Advance(segment);
                var before = _total;
                _segments[(int)(_head % _segments.Length)]++;
                _total++;
                return (before, _total);
            }
        }

        public bool IsIdle(long segment)
        {
            lock (_segments)
            {
                Advance(segment);
                return _total == 0;
            }
        }

        private void Advance(long segment)
        {
            if (_head < 0) { _head = segment; return; }
            // A clock that steps backwards keeps counting in the current segment.
            if (segment <= _head) return;

            if (segment - _head >= _segments.Length)
            {
                Array.Clear(_segments);
                _total = 0;
            }
            else
            {
                for (var s = _head + 1; s <= segment; s++)
                {
                    var i = (int)(s % _segments.Length);
                    _total -= _segments[i];
                    _segments[i] = 0;
                }
            }
            _head = segment;
        }
    }
}
