using System.Diagnostics.Metrics;

namespace AMR.DeliveryPlanning.SharedKernel.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible business-level metrics for the order-to-dispatch
/// workflow. Companion to <c>ProjectionMetrics</c> (which covers read-side
/// health); this one surfaces write-side workflow health that ops can alert
/// on without reading application logs.
///
/// Exposes (Meter name <c>DTMS.Workflow</c>):
///   - dtms.workflow.orders_stuck_planned          (gauge, set by the watchdog)
///   - dtms.workflow.consumer_retry_total          (counter)
///   - dtms.workflow.consumer_faulted_total        (counter)
///   - dtms.workflow.consumer_dispatch_exception_total (counter)
///   - dtms.workflow.outbox_pending                (gauge, set by the outbox processor)
///   - dtms.workflow.outbox_age_seconds            (histogram)
///   - dtms.workflow.watchdog_replays_total        (counter)
///
/// Drives the SLO alerts defined in the Tier 1 plan:
///   - orders_stuck_planned &gt; 0 for 5 minutes (P1)
///   - consumer_faulted rate &gt; 0.1/s for 10 minutes (P2)
///   - outbox_age_seconds &gt; 120 (P2)
/// </summary>
public sealed class WorkflowMetrics : IDisposable
{
    public const string MeterName = "DTMS.Workflow";

    private readonly Meter _meter;
    private readonly Counter<long> _consumerRetry;
    private readonly Counter<long> _consumerFaulted;
    private readonly Counter<long> _dispatchException;
    private readonly Counter<long> _watchdogReplays;
    private readonly Histogram<double> _outboxAge;

    // Observable gauges read from a snapshot the producer (watchdog, outbox
    // processor) updates. Using a snapshot rather than a callback keeps the
    // producer code synchronous and lets multiple producers contribute without
    // coordinating on the meter.
    private long _ordersStuckPlanned;
    private long _outboxPending;

    public WorkflowMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _consumerRetry = _meter.CreateCounter<long>(
            "dtms.workflow.consumer_retry_total",
            unit: "events",
            description: "Count of MassTransit consumer message retries (in-process UseMessageRetry).");

        _consumerFaulted = _meter.CreateCounter<long>(
            "dtms.workflow.consumer_faulted_total",
            unit: "events",
            description: "Count of consumer messages that exhausted retries and went to the error queue.");

        _dispatchException = _meter.CreateCounter<long>(
            "dtms.workflow.consumer_dispatch_exception_total",
            unit: "events",
            description: "Count of DispatchByRouteAsync exceptions caught by the planning consumer (T1.2).");

        _watchdogReplays = _meter.CreateCounter<long>(
            "dtms.workflow.watchdog_replays_total",
            unit: "events",
            description: "Count of stuck orders re-published by the Planning reconciliation watchdog (T1.4).");

        _outboxAge = _meter.CreateHistogram<double>(
            "dtms.workflow.outbox_age_seconds",
            unit: "s",
            description: "Age of outbox messages at publish time (occurrence-time to publish-time).");

        _meter.CreateObservableGauge(
            "dtms.workflow.orders_stuck_planned",
            () => Interlocked.Read(ref _ordersStuckPlanned),
            unit: "orders",
            description: "Orders at Status=Planned with no Trip row, older than the stale threshold (set by watchdog).");

        _meter.CreateObservableGauge(
            "dtms.workflow.outbox_pending",
            () => Interlocked.Read(ref _outboxPending),
            unit: "messages",
            description: "Outbox messages with ProcessedOnUtc=null (set by the outbox processor).");
    }

    public void RecordConsumerRetry(string consumerType)
        => _consumerRetry.Add(1, Tag("consumer", consumerType));

    public void RecordConsumerFaulted(string consumerType)
        => _consumerFaulted.Add(1, Tag("consumer", consumerType));

    public void RecordDispatchException(string exceptionType)
        => _dispatchException.Add(1, Tag("exception", exceptionType));

    public void RecordWatchdogReplay(string reason)
        => _watchdogReplays.Add(1, Tag("reason", reason));

    public void RecordOutboxAge(double ageSeconds)
    {
        if (ageSeconds < 0) ageSeconds = 0;
        _outboxAge.Record(ageSeconds);
    }

    public void SetOrdersStuckPlanned(long count)
        => Interlocked.Exchange(ref _ordersStuckPlanned, Math.Max(0, count));

    public void SetOutboxPending(long count)
        => Interlocked.Exchange(ref _outboxPending, Math.Max(0, count));

    private static KeyValuePair<string, object?> Tag(string key, string value)
        => new(key, value);

    public void Dispose() => _meter.Dispose();
}
