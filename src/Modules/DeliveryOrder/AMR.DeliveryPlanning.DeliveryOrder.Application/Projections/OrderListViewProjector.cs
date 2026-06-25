using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P4 — Materializes the denormalized OrderListView read model.
/// Subscribes to:
///   - DeliveryOrder lifecycle (11 events) — creates the row on
///     Confirmed (event carries the Items snapshot for SearchText),
///     status-updates on every other transition.
///   - Trip lifecycle (TripStarted/Failed/Cancelled/Completed) —
///     recomputes <c>HasFailedTrip</c> + <c>LatestTripId</c>.
///   - Job lifecycle (JobCreated/Dispatched/Executing/Completed/
///     Failed/Cancelled) — recomputes <c>HasActiveJob</c> +
///     <c>LatestJobStatus</c>.
///
/// <para><b>Coverage gap (MVP):</b> the derived Trip/Job booleans are
/// computed from the event in hand, not by re-querying. That means
/// HasFailedTrip turns true on the first TripFailed event for the
/// order and stays true forever (operator-recoverable but never
/// auto-cleared). HasActiveJob mirrors the latest JobStatus seen.
/// This is intentional — MVP keeps the projector self-contained.
/// Phase P4.5 can add cross-projection reads if ops needs the
/// booleans to be "currently true" rather than "ever true".</para>
/// </summary>
public class OrderListViewProjector :
    // Order lifecycle ────────────────────────────────────────────────────
    IConsumer<DeliveryOrderCreatedIntegrationEventV1>,
    IConsumer<DeliveryOrderSubmittedIntegrationEventV1>,
    IConsumer<DeliveryOrderValidatedIntegrationEventV1>,
    IConsumer<DeliveryOrderConfirmedIntegrationEventV1>,
    IConsumer<DeliveryOrderDispatchedIntegrationEventV1>,
    IConsumer<DeliveryOrderInProgressIntegrationEventV1>,
    IConsumer<DeliveryOrderCompletedIntegrationEventV1>,
    IConsumer<DeliveryOrderPartiallyCompletedIntegrationEventV1>,
    IConsumer<DeliveryOrderFailedIntegrationEventV1>,
    IConsumer<DeliveryOrderCancelledIntegrationEventV1>,
    IConsumer<DeliveryOrderRejectedIntegrationEventV1>,
    IConsumer<DeliveryOrderHeldIntegrationEventV1>,
    IConsumer<DeliveryOrderReleasedIntegrationEventV1>,
    IConsumer<DeliveryOrderAmendedIntegrationEventV1>,
    IConsumer<DeliveryOrderDraftUpdatedIntegrationEventV1>,
    // Trip lifecycle ─────────────────────────────────────────────────────
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>,
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripStartedIntegrationEvent>,
    // Job lifecycle ──────────────────────────────────────────────────────
    IConsumer<JobCreatedIntegrationEventV1>,
    IConsumer<JobDispatchedIntegrationEventV1>,
    IConsumer<JobExecutingIntegrationEventV1>,
    IConsumer<JobCompletedIntegrationEventV1>,
    IConsumer<JobFailedIntegrationEventV1>,
    IConsumer<JobCancelledIntegrationEventV1>
{
    public const string Name = nameof(OrderListViewProjector);

    private static readonly HashSet<string> ActiveJobStatuses = new()
    {
        "Created", "Assigned", "Committed", "Dispatched", "Executing",
    };

    private readonly IOrderListViewProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly IOrderRealtimePublisher _realtime;
    private readonly ILogger<OrderListViewProjector> _logger;

    public OrderListViewProjector(
        IOrderListViewProjectionStore store,
        ProjectionMetrics metrics,
        IOrderRealtimePublisher realtime,
        ILogger<OrderListViewProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _realtime = realtime;
        _logger = logger;
    }

    // ── Order lifecycle ────────────────────────────────────────────────
    // Phase P4.6 — every Order event triggers a full refresh from the
    // aggregate. Event payload is no longer trusted as the source of
    // truth — it's just a "this order changed, re-read it" trigger.
    // Benefits: lossy payloads can't drift, replay is idempotent, and
    // adding new projection columns only touches the store's mapper.

    public Task Consume(ConsumeContext<DeliveryOrderCreatedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, ctx.Message.Status,
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderSubmittedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Submitted",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderValidatedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Validated",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Confirmed",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Dispatched",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "InProgress",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Completed",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "PartiallyCompleted",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Failed",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Cancelled",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Rejected",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Held",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Confirmed",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    // Phase P4.6 — non-status mutations still need a projection refresh.
    // Amend changes ServiceWindow / Priority; DraftUpdated replaces items
    // + totals. Status itself doesn't move, but display columns do, so
    // the same refresh-from-aggregate path keeps them in lockstep.

    public Task Consume(ConsumeContext<DeliveryOrderAmendedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "Amended",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderDraftUpdatedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "DraftUpdated",
            () => _store.RefreshFromAggregateAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    // ── Trip lifecycle ──────────────────────────────────────────────────
    // Trip + Job events don't change OrderStatus itself — the changeHint
    // mirrors the trip/job state so the SignalR payload stays informative
    // (the frontend treats it as opaque "refetch please" anyway).

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "TripFailed", () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: true, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "TripCancelled", () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: true, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "TripCompleted", () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: false, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "TripStarted", () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: false, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    // ── Job lifecycle ──────────────────────────────────────────────────

    public Task Consume(ConsumeContext<JobCreatedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "JobCreated", () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: true, latestJobStatus: "Created", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobDispatchedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "JobDispatched", () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: true, latestJobStatus: "Dispatched", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobExecutingIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "JobExecuting", () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: true, latestJobStatus: "Executing", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobCompletedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "JobCompleted", () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: false, latestJobStatus: "Completed", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobFailedIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "JobFailed", () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: false, latestJobStatus: "Failed", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobCancelledIntegrationEventV1> ctx)
        => Run(ctx, ctx.Message.DeliveryOrderId, "JobCancelled", () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: false, latestJobStatus: "Cancelled", ctx.CancellationToken));

    // ── Core projection wrapper: dedup + metrics + failure split ───────

    private async Task Run<TEvent>(
        ConsumeContext<TEvent> ctx,
        Guid orderId,
        string changeHint,
        Func<Task> body)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ctx.CancellationToken))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            return;
        }

        try
        {
            await body();
            await _store.MarkProcessedAsync(Name, evt.EventId, ctx.CancellationToken);
            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            // Phase P4 — hint cross-order list page to refetch. Pushed
            // AFTER MarkProcessed succeeds so a redelivery + dedup-skip
            // doesn't fire a duplicate push. Failures are swallowed by
            // the publisher (UI catches up on next REST refresh).
            await _realtime.PublishOrderListChangedAsync(orderId, changeHint, ctx.CancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient projection failure — will retry");
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordPermanentFailure(Name, typeof(TEvent).Name);
            _logger.LogError(ex,
                "Permanent projection failure for {EventType} {EventId} — event dropped",
                typeof(TEvent).Name, evt.EventId);
        }
    }

    private static bool IsTransient(Exception ex) => ex is
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException or
        TimeoutException or
        TaskCanceledException;
}
