using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P1 — Materializes Order status transitions into the
/// deliveryorder.OrderStatusHistory read model. Subscribes to every
/// DeliveryOrder integration event whose semantics imply a status change.
///
/// Coverage gaps (deliberate — no integration event exists today; will be
/// added in P0.2 hardening if needed):
///   - Submitted, Validated, Planning, Planned
///     (Planning + Planned are deliberately internal-only per
///     DeliveryOrderStatusIntegrationEvents.cs comment.)
///
/// Idempotent + out-of-order safe:
///   - Inbox check skips duplicate redeliveries.
///   - Events older than the latest recorded transition are dropped with
///     a warning (decision logged in docs/event-projection-plan.md).
/// </summary>
public class OrderStatusHistoryProjector :
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
    IConsumer<DeliveryOrderAmendedIntegrationEventV1>
{
    public const string Name = nameof(OrderStatusHistoryProjector);

    private readonly IOrderStatusHistoryProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<OrderStatusHistoryProjector> _logger;

    public OrderStatusHistoryProjector(
        IOrderStatusHistoryProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<OrderStatusHistoryProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    // ── IConsumer<TEvent> implementations — each one decodes the event
    //    into (orderId, toStatus, reason) and delegates to the shared
    //    Project method. Keep these small + symmetrical so a reviewer can
    //    eyeball the status mapping at a glance.

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Confirmed", reason: null);

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Dispatched", reason: null);

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "InProgress", reason: null);

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Completed", reason: null);

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "PartiallyCompleted",
            reason: $"{ctx.Message.DeliveredCount}/{ctx.Message.TotalItems} delivered");

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Failed", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Cancelled", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Rejected", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Held", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        // Release returns the order to Confirmed (per DeliveryOrder.cs Release method).
        => Project(ctx, ctx.Message.DeliveryOrderId, "Confirmed", reason: "Released from Held");

    public Task Consume(ConsumeContext<DeliveryOrderAmendedIntegrationEventV1> ctx)
        // Amend doesn't move Status, but it's an interesting timeline event —
        // record under a synthetic "Amended" marker so the timeline shows it.
        => Project(ctx, ctx.Message.DeliveryOrderId, "Amended", ctx.Message.Reason);

    private async Task Project<TEvent>(
        ConsumeContext<TEvent> ctx, Guid orderId, string toStatus, string? reason)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
            ["OrderId"] = orderId,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            var latest = await _store.GetLatestForOrderAsync(orderId, ct);

            // Out-of-order guard — drop strictly older events. Equal timestamps
            // are allowed through (two events at the same instant are rare but
            // legal; ordering by id resolves ties on read).
            if (latest is { } prev && evt.OccurredOn < prev.OccurredAt)
            {
                _metrics.RecordPermanentFailure(Name, typeof(TEvent).Name);
                _logger.LogWarning(
                    "Out-of-order event {EventId} for Order {OrderId} skipped " +
                    "(event time {EventTime:O} < latest recorded {LatestTime:O})",
                    evt.EventId, orderId, evt.OccurredOn, prev.OccurredAt);
                return;
            }

            var fromStatus = latest?.ToStatus;

            await _store.AppendAsync(
                Name, evt.EventId, orderId, fromStatus, toStatus,
                evt.OccurredOn, reason, ct);

            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            _logger.LogInformation(
                "Projected {EventType} for Order {OrderId}: {From}→{To}",
                typeof(TEvent).Name, orderId, fromStatus ?? "(initial)", toStatus);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            // Throw → MassTransit retries per outbox policy.
            _logger.LogWarning(ex, "Transient projection failure — will retry");
            throw;
        }
        catch (Exception ex)
        {
            // Permanent — log richly + drop. Future: DLQ inspection UI (see
            // docs/event-projection-plan.md §P0 deferred).
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
