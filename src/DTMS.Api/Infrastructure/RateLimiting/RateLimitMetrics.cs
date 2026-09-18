using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace DTMS.Api.Infrastructure.RateLimiting;

/// <summary>
/// Rate-limit metrics, meter <c>DTMS.RateLimit</c>. Tagged by traffic class
/// only — never by caller, which would create one series per user.
///
/// Exposed metrics (Prometheus names):
///   - dtms_ratelimit_requests_total{class}
///   - dtms_ratelimit_rejected_total{class}      — actually refused with 429
///   - dtms_ratelimit_would_reject_total{class}  — over quota while not enforcing
///   - dtms_ratelimit_near_limit_total{class}    — a caller crossed the near-limit share
///   - dtms_ratelimit_partition_peak{class}      — busiest single caller's window count, last 1-2 min
///
/// Size a quota from <c>max_over_time(dtms_ratelimit_partition_peak[1d])</c>.
/// </summary>
public sealed class RateLimitMetrics : IDisposable
{
    public const string MeterName = "DTMS.RateLimit";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _wouldReject;
    private readonly Counter<long> _nearLimit;
    private readonly ConcurrentDictionary<TrafficClass, PeakWindow> _peaks = new();
    private readonly TimeProvider _time;

    public RateLimitMetrics() : this(TimeProvider.System) { }

    public RateLimitMetrics(TimeProvider time)
    {
        _time = time;
        _meter = new Meter(MeterName, "1.0.0");

        _requests = _meter.CreateCounter<long>(
            "dtms.ratelimit.requests.total",
            description: "Requests seen by the rate limiter, by traffic class.");
        _rejected = _meter.CreateCounter<long>(
            "dtms.ratelimit.rejected.total",
            description: "Requests refused with 429.");
        _wouldReject = _meter.CreateCounter<long>(
            "dtms.ratelimit.would_reject.total",
            description: "Requests over their class quota while the per-class limits are not enforced.");
        _nearLimit = _meter.CreateCounter<long>(
            "dtms.ratelimit.near_limit.total",
            description: "Times a single caller's window count crossed the near-limit share of its quota.");

        // A peak held over the current and previous minute, not reset on read:
        // Prometheus and the OTLP exporter both collect, and a reset-on-read
        // gauge would hand the peak to whichever reader came first.
        _meter.CreateObservableGauge(
            "dtms.ratelimit.partition_peak",
            ObservePeaks,
            description: "Highest window count of any single caller in the last one to two minutes.");
    }

    public void Request(TrafficClass c) => _requests.Add(1, Tag(c));
    public void Rejected(TrafficClass c) => _rejected.Add(1, Tag(c));
    public void WouldReject(TrafficClass c) => _wouldReject.Add(1, Tag(c));
    public void NearLimit(TrafficClass c) => _nearLimit.Add(1, Tag(c));

    public void WindowCount(TrafficClass c, int count)
        => _peaks.GetOrAdd(c, _ => new PeakWindow()).Record(CurrentMinute(), count);

    private IEnumerable<Measurement<long>> ObservePeaks()
    {
        var minute = CurrentMinute();
        foreach (var (c, peak) in _peaks)
            yield return new Measurement<long>(peak.Read(minute), Tag(c));
    }

    private long CurrentMinute() => _time.GetUtcNow().ToUnixTimeSeconds() / 60;

    private static KeyValuePair<string, object?> Tag(TrafficClass c)
        => new("class", c.ToString().ToLowerInvariant());

    public void Dispose() => _meter.Dispose();

    private sealed class PeakWindow
    {
        private long _minute;
        private long _current;
        private long _previous;

        public void Record(long minute, long value)
        {
            lock (this)
            {
                Roll(minute);
                if (value > _current) _current = value;
            }
        }

        public long Read(long minute)
        {
            lock (this)
            {
                Roll(minute);
                return Math.Max(_current, _previous);
            }
        }

        private void Roll(long minute)
        {
            if (minute == _minute) return;
            _previous = minute == _minute + 1 ? _current : 0;
            _current = 0;
            _minute = minute;
        }
    }
}
