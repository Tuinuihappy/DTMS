using System.Diagnostics.Metrics;

namespace DTMS.Api.Infrastructure.Metrics;

/// <summary>
/// WMS PR-4b (PR-H) — Operator pool metrics. Powers the ops dashboard for
/// pool health (depth, claim outcomes, wait time, broadcaster failures).
/// Meter name <c>DTMS.Pool</c> is registered in <c>Program.cs</c> so the
/// OTel collector subscribes to it automatically.
///
/// Exposed metrics:
///   - dtms.pool.depth (gauge, from PoolDepthPollingService every 10 s)
///   - dtms.pool.claim.total{outcome=success|conflict|error}
///   - dtms.pool.claim.latency_ms (histogram of AcknowledgePoolCommandHandler duration)
///   - dtms.pool.wait_seconds (histogram of ClaimedAt − DispatchedAt on successful claim)
///   - dtms.pool.broadcast.total{event=added|claimed|removed, outcome=sent|failed}
///
/// Grafana dashboard JSON lives under docs/grafana/operator-pool.json (PR-H).
/// </summary>
public sealed class PoolMetrics : IDisposable
{
    public const string MeterName = "DTMS.Pool";

    private readonly Meter _meter;
    private readonly Counter<long> _claimTotal;
    private readonly Histogram<double> _claimLatencyMs;
    private readonly Histogram<double> _waitSeconds;
    private readonly Counter<long> _broadcastTotal;

    // depth gauge is registered via ObservableGauge below; the callback
    // reads a shared long the polling service updates. Using
    // ObservableGauge keeps the collector-side model simple (no manual
    // scrape); the polling service just refreshes _depthSnapshot.
    private long _depthSnapshot;
    private readonly ObservableGauge<long> _depthGauge;

    public PoolMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Units omitted intentionally — the OTel Prometheus exporter
        // appends the unit to the exported metric name, producing
        // names like `dtms_pool_depth_trips` that drift from the
        // OTel spec's dotted name and force downstream dashboards to
        // know both spellings. Keeping the metric name self-describing
        // (`latency_ms`, `wait_seconds`) is clearer for our size of
        // metric catalog.
        _depthGauge = _meter.CreateObservableGauge(
            "dtms.pool.depth",
            () => _depthSnapshot,
            description: "Number of Manual/Fleet trips currently in the pool (Status=Created ∧ DispatchedAt≠null ∧ ClaimedByOperatorId IS NULL).");

        _claimTotal = _meter.CreateCounter<long>(
            "dtms.pool.claim.total",
            description: "Pool acknowledge outcomes. success=CAS won; conflict=CAS lost (409); error=all other failures.");

        _claimLatencyMs = _meter.CreateHistogram<double>(
            "dtms.pool.claim.latency_ms",
            description: "AcknowledgeTripCommandHandler pool-path duration.");

        _waitSeconds = _meter.CreateHistogram<double>(
            "dtms.pool.wait_seconds",
            description: "Time from dispatch to successful claim (ClaimedAt − DispatchedAt).");

        _broadcastTotal = _meter.CreateCounter<long>(
            "dtms.pool.broadcast.total",
            description: "SignalR pool broadcasts fired. outcome=failed means the hub threw and we swallowed.");
    }

    public void SetDepth(long depth) => _depthSnapshot = depth;

    public void RecordClaim(string outcome, double latencyMs)
    {
        _claimTotal.Add(1, Tag("outcome", outcome));
        _claimLatencyMs.Record(latencyMs, Tag("outcome", outcome));
    }

    public void RecordWait(double waitSeconds) => _waitSeconds.Record(waitSeconds);

    public void RecordBroadcast(string @event, string outcome)
        => _broadcastTotal.Add(1, Tag("event", @event), Tag("outcome", outcome));

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    public void Dispose() => _meter.Dispose();
}
