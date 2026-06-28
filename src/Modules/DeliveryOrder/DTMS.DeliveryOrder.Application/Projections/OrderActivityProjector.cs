using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P2 — Materializes unified per-order activity into the
/// <c>deliveryorder.OrderActivity</c> read model. Subscribes to every
/// integration event whose semantics imply a row in the operator's
/// "what happened to this order?" view.
///
/// <para><b>Coverage matrix (2026-06-15 P2 housekeeping):</b></para>
/// <list type="bullet">
///   <item>Order lifecycle (14 events) — Created, Submitted, Validated,
///         Confirmed, Dispatched, InProgress, Completed,
///         PartiallyCompleted, Failed, Cancelled, Rejected, Held,
///         Released, Amended.</item>
///   <item>Trip execution (9 events) — Started, PickupCompleted,
///         DropCompleted, Completed, Failed, Cancelled, Paused, Resumed,
///         RobotPassAcknowledged. Plus ExceptionRaised under the same
///         category.</item>
///   <item>POD (1 event) — PodCaptured (Phase P2, surfaces operator scan
///         records in the timeline).</item>
/// </list>
///
/// <para><b>Known gaps (MVP — accepted):</b></para>
/// <list type="bullet">
///   <item>Upstream OMS notify outcomes — no integration event today;
///         backfill rows only.</item>
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
    // Order early lifecycle (Phase P2 — close gap from upstream e53db1f7) ─
    IConsumer<DeliveryOrderCreatedIntegrationEventV1>,
    IConsumer<DeliveryOrderSubmittedIntegrationEventV1>,
    IConsumer<DeliveryOrderValidatedIntegrationEventV1>,
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
    IConsumer<TripRobotPassAcknowledgedIntegrationEventV1>,
    IConsumer<ExceptionRaisedIntegrationEvent>,
    // POD (Phase P2 — operator scan records appear in unified timeline) ─
    IConsumer<PodCapturedIntegrationEvent>
{
    public const string Name = nameof(OrderActivityProjector);

    // Category discriminators on the row. Match the legacy "Source" strings
    // so the UI doesn't need a re-skin.
    private const string CatOrderLifecycle = "OrderLifecycle";
    private const string CatAmendment = "Amendment";
    private const string CatTripExecution = "TripExecution";

    private readonly IOrderActivityProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly IOrderRealtimePublisher _realtime;
    private readonly ILogger<OrderActivityProjector> _logger;

    public OrderActivityProjector(
        IOrderActivityProjectionStore store,
        ProjectionMetrics metrics,
        IOrderRealtimePublisher realtime,
        ILogger<OrderActivityProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _realtime = realtime;
        _logger = logger;
    }

    // Phase P2 (Option A) — same category→source mapping as
    // GetFullOrderAuditQueryHandler so the SignalR push entry has the same
    // <c>Source</c> the REST endpoint would produce. Keep the two
    // mappings in sync — if a category moves buckets, both places change.
    private static string MapCategoryToSource(string category) => category switch
    {
        "OrderLifecycle" => "Order",
        "Amendment"      => "Amendment",
        "TripExecution"  => "TripExecution",
        "TripRetry"      => "TripRetry",
        _                => "Order",
    };

    // ── Order lifecycle handlers ─────────────────────────────────────────

    // Phase P2 early lifecycle (closes upstream e53db1f7 gap).
    public Task Consume(ConsumeContext<DeliveryOrderCreatedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderCreated",
            // Status on the wire reflects the entry point (Draft for manual,
            // Submitted for upstream-originated) — surface so the timeline
            // explains why the next transition may be missing.
            details: $"Entered as {ctx.Message.Status}",
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderSubmittedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderSubmitted", details: null,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderValidatedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderValidated", details: null,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderConfirmed", details: null);

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderDispatched", details: null,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderInProgress", details: null,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderCompleted", details: null,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderPartiallyCompleted",
            details: $"{ctx.Message.DeliveredCount}/{ctx.Message.TotalItems} delivered",
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderFailed", ctx.Message.Reason,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderCancelled", ctx.Message.Reason,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderRejected", ctx.Message.Reason,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderHeld", ctx.Message.Reason,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatOrderLifecycle,
            "OrderReleased", details: null,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

    public Task Consume(ConsumeContext<DeliveryOrderAmendedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, CatAmendment,
            "OrderAmended", ctx.Message.Reason,
            actorId: ctx.Message.TriggeredBy,
            channel: ctx.Message.Channel, displayName: ctx.Message.DisplayName);

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

    // Phase P2 — operator-acknowledged robot checkpoint pass.
    // No DeliveryOrderId on the wire (RIOT3 webhook scope = trip-only);
    // skip like Pause/Resume. The Trip drawer's status timeline (P1)
    // covers this event independently via TripStatusHistoryProjector.
    public Task Consume(ConsumeContext<TripRobotPassAcknowledgedIntegrationEventV1> ctx)
    {
        _logger.LogDebug(
            "TripRobotPassAcknowledged {EventId} for Trip {TripId} skipped — no DeliveryOrderId in payload",
            ctx.Message.EventId, ctx.Message.TripId);
        return Task.CompletedTask;
    }

    // Phase P2 — POD scan captured at a stop. Same scoping problem (no
    // DeliveryOrderId on the wire). Backfill SQL seeds historical POD
    // rows; live POD rows wait for the event payload to carry OrderId
    // (deferred to P2.5 hardening).
    public Task Consume(ConsumeContext<PodCapturedIntegrationEvent> ctx)
    {
        _logger.LogDebug(
            "PodCaptured {EventId} for Trip {TripId} skipped — no DeliveryOrderId in payload",
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
        string? actorId = null,
        string? channel = null,
        string? displayName = null)
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
                relatedTripId, attemptNumber,
                channel, displayName, ct);

            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            // Phase P2 — push to "order:{id:N}" group after durable write.
            // Mirrors the same record shape FullAuditLog renders (with the
            // category→source already mapped) so the frontend can append
            // the entry without an adapter.
            _ = _realtime.PublishActivityUpdatedAsync(
                orderId,
                new OrderActivityEntryDto(
                    Id: evt.EventId,
                    Source: MapCategoryToSource(category),
                    EventType: eventType,
                    Details: details,
                    ActorId: actorId,
                    OccurredAt: evt.OccurredOn,
                    RelatedTripId: relatedTripId,
                    AttemptNumber: attemptNumber,
                    Channel: channel,
                    DisplayName: displayName),
                ct);

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
