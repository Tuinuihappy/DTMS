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
    private readonly ILogger<OrderListViewProjector> _logger;

    public OrderListViewProjector(
        IOrderListViewProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<OrderListViewProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    // ── Order lifecycle: Created materializes the row with full snapshot ─

    public Task Consume(ConsumeContext<DeliveryOrderCreatedIntegrationEventV1> ctx)
        => Run(ctx, async () =>
        {
            var m = ctx.Message;
            // SearchText concatenates the per-Order free-text fields +
            // every item id the event carries. The DB derives the
            // tsvector via the generated column.
            var itemText = string.Join(' ',
                m.Items.Select(i => i.ItemId).Where(s => !string.IsNullOrEmpty(s)));
            var search = string.Join(' ', new[] {
                m.DeliveryOrderId.ToString("N"),
                m.OrderRef,
                itemText,
            }.Where(s => !string.IsNullOrEmpty(s)));

            await _store.UpsertOnCreateAsync(
                orderId: m.DeliveryOrderId,
                orderRef: m.OrderRef,
                status: m.Status,
                sourceSystem: m.SourceSystem,
                priority: m.Priority,
                transportMode: m.RequestedTransportMode,
                requestedBy: m.RequestedBy, createdBy: m.CreatedBy, notes: m.Notes,
                totalItems: m.TotalItems,
                totalQuantity: m.TotalQuantity,
                totalWeightKg: m.TotalWeightKg,
                requiresDropPod: m.RequiresDropPod, requiresPickupPod: m.RequiresPickupPod,
                createdAt: m.OccurredOn,
                submittedAt: m.SubmittedAt,
                serviceWindowEarliestUtc: m.EarliestUtc,
                serviceWindowLatestUtc: m.LatestUtc,
                searchText: search,
                ctx.CancellationToken);
        });

    // ── Order lifecycle: other transitions just update the status ──────

    public Task Consume(ConsumeContext<DeliveryOrderSubmittedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Submitted", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderValidatedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Validated", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Confirmed", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Dispatched", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "InProgress", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Completed", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "PartiallyCompleted", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Failed", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Cancelled", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Rejected", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Held", ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpdateStatusAsync(ctx.Message.DeliveryOrderId, "Confirmed", ctx.Message.OccurredOn, ctx.CancellationToken));

    // ── Trip lifecycle ──────────────────────────────────────────────────

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: true, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: true, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: false, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetTripDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasFailedTrip: false, latestTripId: ctx.Message.TripId, ctx.CancellationToken));

    // ── Job lifecycle ──────────────────────────────────────────────────

    public Task Consume(ConsumeContext<JobCreatedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: true, latestJobStatus: "Created", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobDispatchedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: true, latestJobStatus: "Dispatched", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobExecutingIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: true, latestJobStatus: "Executing", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobCompletedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: false, latestJobStatus: "Completed", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobFailedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: false, latestJobStatus: "Failed", ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobCancelledIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetJobDerivedFieldsAsync(
            ctx.Message.DeliveryOrderId, hasActiveJob: false, latestJobStatus: "Cancelled", ctx.CancellationToken));

    // ── Core projection wrapper: dedup + metrics + failure split ───────

    private async Task Run<TEvent>(ConsumeContext<TEvent> ctx, Func<Task> body)
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
