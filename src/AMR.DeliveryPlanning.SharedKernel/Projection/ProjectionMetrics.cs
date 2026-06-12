using System.Diagnostics.Metrics;

namespace AMR.DeliveryPlanning.SharedKernel.Projection;

/// <summary>
/// OpenTelemetry-compatible metrics for every projector in DTMS. Wired
/// once at app startup as a singleton; consumed by
/// <see cref="IdempotentProjector{TEvent}"/>.
///
/// Exposes:
///   - dtms.projection.events_projected_total{projector, event_type}
///   - dtms.projection.lag_seconds{projector}
///   - dtms.projection.dedup_skipped_total{projector, event_type}
///   - dtms.projection.permanent_failures_total{projector, event_type}
///
/// The Meter name <c>DTMS.Projection</c> is also what the OTel collector
/// subscribes to via <c>AddMeter("DTMS.Projection")</c> in Program.cs.
/// </summary>
public sealed class ProjectionMetrics : IDisposable
{
    public const string MeterName = "DTMS.Projection";

    private readonly Meter _meter;
    private readonly Counter<long> _projected;
    private readonly Counter<long> _dedupSkipped;
    private readonly Counter<long> _permanentFailures;
    private readonly Histogram<double> _lagSeconds;

    public ProjectionMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _projected = _meter.CreateCounter<long>(
            "dtms.projection.events_projected_total",
            unit: "events",
            description: "Count of integration events successfully projected to a read model.");

        _dedupSkipped = _meter.CreateCounter<long>(
            "dtms.projection.dedup_skipped_total",
            unit: "events",
            description: "Count of duplicate events skipped via the projector inbox.");

        _permanentFailures = _meter.CreateCounter<long>(
            "dtms.projection.permanent_failures_total",
            unit: "events",
            description: "Count of non-transient projection failures (event dropped, not retried).");

        _lagSeconds = _meter.CreateHistogram<double>(
            "dtms.projection.lag_seconds",
            unit: "s",
            description: "Time between event occurrence and successful projection (event-time to processing-time).");
    }

    public void RecordProjected(string projectorName, string eventType)
        => _projected.Add(1, Tag("projector", projectorName), Tag("event_type", eventType));

    public void RecordDedupSkipped(string projectorName, string eventType)
        => _dedupSkipped.Add(1, Tag("projector", projectorName), Tag("event_type", eventType));

    public void RecordPermanentFailure(string projectorName, string eventType)
        => _permanentFailures.Add(1, Tag("projector", projectorName), Tag("event_type", eventType));

    public void RecordLag(string projectorName, DateTime eventOccurredOn)
    {
        var lagSeconds = (DateTime.UtcNow - eventOccurredOn).TotalSeconds;
        // Clamp negative lag (clock skew) so the histogram bucket distribution
        // doesn't get poisoned — log the anomaly separately.
        if (lagSeconds < 0) lagSeconds = 0;
        _lagSeconds.Record(lagSeconds, Tag("projector", projectorName));
    }

    private static KeyValuePair<string, object?> Tag(string key, string value)
        => new(key, value);

    public void Dispose() => _meter.Dispose();
}
