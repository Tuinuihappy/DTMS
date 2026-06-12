using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P2 — Materializes unified per-order activity into the
/// <c>deliveryorder.OrderActivity</c> read model. Subscribes to every
/// integration event whose semantics imply a row in the operator's
/// "what happened to this order?" view.
///
/// <para><b>Coverage matrix:</b></para>
/// <list type="bullet">
///   <item>Order lifecycle (11 events) — full coverage via DeliveryOrder
///         integration events.</item>
///   <item>Trip execution (8 events) — full coverage via Dispatch
///         integration events.</item>
/// </list>
///
/// <para><b>Known gaps (MVP — accepted):</b></para>
/// <list type="bullet">
///   <item>Item POD scans — written to OrderAuditEvent only, no
///         integration event. Historical rows seeded via backfill SQL;
///         new scans don't appear in the timeline until POD events
///         are added under P2.5 hardening.</item>
///   <item>Upstream OMS notify outcomes — same situation.</item>
///   <item>Trip retry triggers — no integration event; backfill only.</item>
///   <item>Admin actions (OrderReopened, OrderAbandoned) — OrderAuditEvent
///         only; backfill only.</item>
/// </list>
///
/// Idempotent + out-of-order tolerant via the standard inbox pattern.
/// Out-of-order events are recorded as-is (rather than skipped like
/// status-history) because the activity timeline doesn't chain
/// FromStatus — readers sort on OccurredAt at query time.
/// </summary>
public class OrderActivityProjector :
    // Order lifecycle ────────────────────────────────────────────────────
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
    // Trip execution ─────────────────────────────────────────────────────
    IConsumer<TripStartedIntegrationEvent>,
    IConsumer<TripPickupCompletedIntegrationEvent>,
    IConsumer<TripDropCompletedIntegrationEvent>,
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>,
    IConsumer<TripPausedIntegrationEventV1>,
    IConsumer<TripResumedIntegrationEventV1>,
    IConsumer<ExceptionRaisedIntegrationEvent>
{
    public const string Name = nameof(OrderActivityProjector);

    // Category discriminators on the row. Match the legacy "Source" strings
    // so the UI doesn't need a re-skin.
    private const string CatOrderLifecycle = "OrderLifecycle";
    private const string CatAmendment = "Amendment";
    private const string CatTripExecution = "TripExecution";

    private readonly IOrderActivityProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<OrderActivityProjector> _logger;

    public OrderActivityProjector(
        IOrderActivityProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<OrderActivityProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    // ── Order lifecycle handlers ─────────────────────────────────────────

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderConfirmed", details: null);

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderDispatched", details: null);

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderInProgress", details: null);

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderCompleted", details: null);

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderPartiallyCompleted",
            details: $"{ctx.Message.DeliveredCount}/{ctx.Message.TotalItems} delivered");

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderFailed", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderCancelled", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderRejected", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderHeld", ctx.Message.Reason);

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderReleased", details: null);

    public Task Consume(ConsumeContext<DeliveryOrderAmendedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatAmendment,
            "OrderAmended", ctx.Message.Reason);

    // ── Trip execution handlers ──────────────────────────────────────────

    public Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatTripExecution,
            "TripStarted",
            details: ctx.Message.VehicleId == Guid.Empty ? null : $"vehicle {ctx.Message.VehicleId}",
            relatedTripId: ctx.Message.TripId);

    public Task Consume(ConsumeContext<TripPickupCompletedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatTripExecution,
            "TripPickupCompleted", details: null,
            relatedTripId: ctx.Message.TripId);

    public Task Consume(ConsumeContext<TripDropCompletedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatTripExecution,
            "TripDropCompleted", details: null,
            relatedTripId: ctx.Message.TripId);

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatTripExecution,
            "TripCompleted",
            details: string.IsNullOrWhiteSpace(ctx.Message.VendorUpperKey) ? null : $"upper={ctx.Message.VendorUpperKey}",
            relatedTripId: ctx.Message.TripId);

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatTripExecution,
            "TripFailed", ctx.Message.Reason,
            relatedTripId: ctx.Message.TripId);

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatTripExecution,
            "TripCancelled", ctx.Message.Reason,
            relatedTripId: ctx.Message.TripId);

    public async Task Consume(ConsumeContext<TripPausedIntegrationEventV1> ctx)
    {
        // Pause/Resume payload doesn't carry DeliveryOrderId. The activity
        // timeline is order-scoped — without an OrderId we have nothing to
        // attach the row to. Skip with a warning rather than fabricating.
        _logger.LogDebug(
            "TripPaused {EventId} for Trip {TripId} skipped — no DeliveryOrderId in payload",
            ctx.Message.EventId, ctx.Message.TripId);
        await Task.CompletedTask;
    }

    public async Task Consume(ConsumeContext<TripResumedIntegrationEventV1> ctx)
    {
        _logger.LogDebug(
            "TripResumed {EventId} for Trip {TripId} skipped — no DeliveryOrderId in payload",
            ctx.Message.EventId, ctx.Message.TripId);
        await Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<ExceptionRaisedIntegrationEvent> ctx)
    {
        // ExceptionRaised carries TripId + JobId but not DeliveryOrderId.
        // Skip for the same reason as Pause/Resume — order scope unknown.
        _logger.LogDebug(
            "ExceptionRaised {EventId} for Trip {TripId} skipped — no DeliveryOrderId in payload",
            ctx.Message.EventId, ctx.Message.TripId);
        return Task.CompletedTask;
    }

    // ── Core projection routine — same shape as P1 projectors ────────────

    private async Task Project<TEvent>(
        ConsumeContext<TEvent> ctx,
        Guid orderId,
        string category,
        string eventType,
        string? details,
        Guid? relatedTripId = null,
        int? attemptNumber = null,
        string? actorId = null)
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
            ["Category"] = category,
        });

        if (orderId == Guid.Empty)
        {
            _logger.LogWarning("Empty OrderId on {EventType} {EventId} — skipped", typeof(TEvent).Name, evt.EventId);
            return;
        }

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            await _store.AppendAsync(
                Name, evt.EventId, orderId, category, eventType,
                details, actorId, evt.OccurredOn,
                relatedTripId, attemptNumber, ct);

            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            _logger.LogInformation(
                "Projected {EventType} for Order {OrderId}: {Category}/{Activity}",
                typeof(TEvent).Name, orderId, category, eventType);
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
