using System.Diagnostics.Metrics;

namespace DTMS.SharedKernel.Diagnostics;

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
///   - dtms.workflow.shutdown_duration_seconds     (histogram, G2 — tag: phase=bus|hosted_services|total)
///   - dtms.workflow.trips_stuck_reconcile         (gauge, set by the AMR reconciler)
///   - dtms.workflow.reconciler_inflight           (gauge, set by the AMR reconciler)
///   - dtms.workflow.reconciler_fetch_error_total  (counter)
///   - dtms.workflow.reconciler_reconciled_total   (counter)
///   - dtms.workflow.reconciler_backfilled_total   (counter — post-terminal vehicle recovery)
///
/// Drives the SLO alerts defined in the Tier 1 plan:
///   - orders_stuck_planned &gt; 0 for 5 minutes (P1)
///   - consumer_faulted rate &gt; 0.1/s for 10 minutes (P2)
///   - outbox_age_seconds &gt; 120 (P2)
///   - trips_stuck_reconcile &gt; 0 for 10 minutes (P1 — AMR order stuck past reconcile window)
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
    private readonly Histogram<double> _shutdownDuration;
    // AMR reconciler health (Riot3ReconciliationService). fetch_error counts
    // trips whose RIOT3 fetch failed this tick (RIOT slow/unreachable) — the
    // leading indicator of vendor connectivity trouble before it escalates.
    private readonly Counter<long> _reconcilerFetchError;
    private readonly Counter<long> _reconcilerReconciled;
    // Trips whose vendor vehicle the reconciler recovered post-terminal
    // (BackfillVendorVehicle — terminal record or self-heal sweep). Counted
    // apart from _reconcilerReconciled: a backfill patches a MISSED capture,
    // it doesn't correct a live-state divergence, so conflating the two would
    // hide how often TASK_PROCESSING is being dropped.
    private readonly Counter<long> _reconcilerBackfilled;

    // Observable gauges read from a snapshot the producer (watchdog, outbox
    // processor) updates. Using a snapshot rather than a callback keeps the
    // producer code synchronous and lets multiple producers contribute without
    // coordinating on the meter.
    private long _ordersStuckPlanned;
    private long _outboxPending;
    // Oldest un-processed outbox row, in seconds, across every schema the
    // processor owns. Distinct from outbox_age_seconds (recorded only on a
    // SUCCESSFUL publish, blind to a row nobody drains). Stored as long ticks-
    // free whole seconds via Interlocked; producer = OutboxProcessorService.
    private long _outboxOldestPendingAgeSeconds;
    // Phase O3 — DLQ size gauge. Set periodically by DlqSizeReporterService.
    private long _outboxDlqSize;
    // AMR reconciler gauges, set each tick by Riot3ReconciliationService.
    // trips_stuck = in-flight envelope trips PAST the reconcile window (created
    // > StaleThresholdHours ago) — these are silently abandoned, the true
    // "order stuck" signal. inflight = trips inside the window being reconciled.
    private long _reconcilerTripsStuck;
    private long _reconcilerInflight;

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

        // G2 — shutdown phase observability. Tag `phase` values:
        //   "total"           — ApplicationStopping → ApplicationStopped (always emitted)
        //   "bus"             — MassTransit IBusObserver.PreStop → PostStop
        //   "hosted_services" — total - bus (computed when both available)
        // Phase G (Grafana dashboards) builds the "Shutdown Phase Distribution"
        // panel + an alert on P95(phase=total) > 60s.
        _shutdownDuration = _meter.CreateHistogram<double>(
            "dtms.workflow.shutdown_duration_seconds",
            unit: "s",
            description: "Time spent in each shutdown phase. Tag: phase=bus|hosted_services|total.");

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

        _meter.CreateObservableGauge(
            "dtms.workflow.outbox_oldest_pending_age_seconds",
            () => Interlocked.Read(ref _outboxOldestPendingAgeSeconds),
            unit: "s",
            description: "Age of the oldest un-processed outbox row across all owned schemas. Reads the backlog directly (unlike outbox_age_seconds, which only records on successful publish), so it surfaces rows nobody drains. Sustained climb = a strand.");

        _meter.CreateObservableGauge(
            "dtms.workflow.outbox_dlq_size",
            () => Interlocked.Read(ref _outboxDlqSize),
            unit: "messages",
            description: "Phase O3 — count of rows in outbox.DeadLetterMessages (set by DlqSizeReporterService every 30s).");

        // AMR reconciler signals. Units intentionally omitted: the OTel
        // Prometheus exporter appends the unit to the metric name, so a
        // `unit: "trips"` here would export `dtms_workflow_trips_stuck_reconcile_trips`
        // and the alert rule would have to know that spelling. Omitting keeps
        // the name stable + self-describing for the rule file + dashboard.
        _reconcilerFetchError = _meter.CreateCounter<long>(
            "dtms.workflow.reconciler_fetch_error_total",
            description: "Trips whose RIOT3 fetch failed during a reconciler tick (RIOT slow/unreachable). Rising = vendor connectivity trouble.");

        _reconcilerReconciled = _meter.CreateCounter<long>(
            "dtms.workflow.reconciler_reconciled_total",
            description: "Trips the reconciler corrected to match RIOT3 state (a dropped webhook it healed).");

        _reconcilerBackfilled = _meter.CreateCounter<long>(
            "dtms.workflow.reconciler_backfilled_total",
            description: "Trips whose vendor vehicle was recovered post-terminal (missed TASK_PROCESSING, filled from RIOT3's terminal record). Rising = webhook loss during the PROCESSING signal.");

        _meter.CreateObservableGauge(
            "dtms.workflow.trips_stuck_reconcile",
            () => Interlocked.Read(ref _reconcilerTripsStuck),
            description: "In-flight envelope trips PAST the reconcile window (created > StaleThresholdHours ago) — silently abandoned by the reconciler. > 0 = orders stuck. Set by Riot3ReconciliationService.");

        _meter.CreateObservableGauge(
            "dtms.workflow.reconciler_inflight",
            () => Interlocked.Read(ref _reconcilerInflight),
            description: "In-flight envelope trips INSIDE the reconcile window being checked each tick. Set by Riot3ReconciliationService.");
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

    public void RecordShutdownDuration(string phase, double seconds)
    {
        if (seconds < 0) seconds = 0;
        _shutdownDuration.Record(seconds, Tag("phase", phase));
    }

    public void SetOrdersStuckPlanned(long count)
        => Interlocked.Exchange(ref _ordersStuckPlanned, Math.Max(0, count));

    public void SetOutboxPending(long count)
        => Interlocked.Exchange(ref _outboxPending, Math.Max(0, count));

    public void SetOutboxOldestPendingAgeSeconds(double ageSeconds)
        => Interlocked.Exchange(ref _outboxOldestPendingAgeSeconds, (long)Math.Max(0, ageSeconds));

    public void SetOutboxDlqSize(long count)
        => Interlocked.Exchange(ref _outboxDlqSize, Math.Max(0, count));

    /// <summary>Emit one reconciler tick's outcome. Counters take the tick's
    /// delta (0 is a valid no-op); gauges take the current snapshot.</summary>
    public void RecordReconcilerTick(long tripsStuck, long inflight, long reconciled, long fetchErrors, long backfilled = 0)
    {
        Interlocked.Exchange(ref _reconcilerTripsStuck, Math.Max(0, tripsStuck));
        Interlocked.Exchange(ref _reconcilerInflight, Math.Max(0, inflight));
        if (reconciled > 0) _reconcilerReconciled.Add(reconciled);
        if (fetchErrors > 0) _reconcilerFetchError.Add(fetchErrors);
        if (backfilled > 0) _reconcilerBackfilled.Add(backfilled);
    }

    private static KeyValuePair<string, object?> Tag(string key, string value)
        => new(key, value);

    public void Dispose() => _meter.Dispose();
}
