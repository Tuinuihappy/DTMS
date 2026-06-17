using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Phase P5.3 — Materializes the (Trip, Item) bindings into
/// dispatch.TripItems so a single GET answers
/// "what items are on this trip + which order owns each?".
///
/// Row lifecycle:
///   TripStarted                         → INSERT 1 row per snapshot item
///   TripPickupCompleted                 → UPDATE ItemStatus = "Picked"
///   TripDropCompleted (POD required)    → UPDATE ItemStatus = "DroppedOff"
///   TripDropCompleted (no POD, V1.2)    → UPDATE ItemStatus = "Delivered"
///   TripCompleted                       → UPDATE ItemStatus = "Delivered"
///   TripFailed / TripCancelled          → UPDATE ItemStatus = "Unbound"
///
/// V1.2 (no-POD branch): TripDropCompletedIntegrationEvent now carries
/// the parent order's RequiresDropPod flag (stamped by the vendor
/// webhook). When it's false, the projector skips the DroppedOff
/// interstitial and writes Delivered directly so the dispatch.TripItems
/// view matches what the DeliveryOrder side already did. Null flag
/// (pre-rollout in-flight events) falls back to DroppedOff — the
/// subsequent TripCompleted still lands the row at Delivered.
///
/// Idempotent (per-EventId inbox) + safe under webhook/reconciler race
/// (InsertBindingsAsync skips ItemPks that already exist for the trip).
/// OrderRef/OrderStatus are snapshotted at trip-start and intentionally
/// not refreshed — operator can re-fetch via the order endpoint if they
/// need live status.
/// </summary>
public class TripItemsProjector :
    IConsumer<TripStartedIntegrationEvent>,
    IConsumer<TripPickupCompletedIntegrationEvent>,
    IConsumer<TripDropCompletedIntegrationEvent>,
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>
{
    public const string Name = nameof(TripItemsProjector);

    private const string StatusPicked = "Picked";
    private const string StatusDroppedOff = "DroppedOff";
    private const string StatusDelivered = "Delivered";
    private const string StatusUnbound = "Unbound";

    private readonly ITripItemsProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<TripItemsProjector> _logger;

    public TripItemsProjector(
        ITripItemsProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<TripItemsProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = nameof(TripStartedIntegrationEvent),
            ["TripId"] = evt.TripId,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, nameof(TripStartedIntegrationEvent));
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            var items = evt.Items ?? Array.Empty<TripItemSnapshot>();
            if (items.Count == 0)
            {
                // Vendor adapter may bind items asynchronously — record the
                // inbox row so we don't reprocess, and surface the deferred
                // binding so operators can spot stuck trips.
                await _store.RecordEmptyBindingAsync(Name, evt.EventId, evt.TripId, evt.OccurredOn, ct);
                _metrics.RecordProjected(Name, nameof(TripStartedIntegrationEvent));
                _metrics.RecordLag(Name, evt.OccurredOn);
                _logger.LogInformation(
                    "Trip {TripId} started with empty item snapshot — inbox recorded, binding deferred",
                    evt.TripId);
                return;
            }

            await _store.InsertBindingsAsync(Name, evt.EventId, evt.TripId, evt.OccurredOn, items, ct);

            _metrics.RecordProjected(Name, nameof(TripStartedIntegrationEvent));
            _metrics.RecordLag(Name, evt.OccurredOn);
            _logger.LogInformation(
                "Projected TripStarted for Trip {TripId}: {ItemCount} items bound",
                evt.TripId, items.Count);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient projection failure — will retry");
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordPermanentFailure(Name, nameof(TripStartedIntegrationEvent));
            _logger.LogError(ex,
                "Permanent projection failure for TripStartedIntegrationEvent {EventId} — event dropped",
                evt.EventId);
        }
    }

    public Task Consume(ConsumeContext<TripPickupCompletedIntegrationEvent> ctx)
        => RefreshItemStatusAsync(ctx, ctx.Message.TripId, StatusPicked, nameof(TripPickupCompletedIntegrationEvent));

    public Task Consume(ConsumeContext<TripDropCompletedIntegrationEvent> ctx)
    {
        // V1.2 — when the order doesn't require POD, drop = delivery.
        // Null flag (pre-rollout) keeps the legacy DroppedOff path so the
        // subsequent TripCompleted still finalizes the row at Delivered.
        var target = ctx.Message.RequiresDropPod == false ? StatusDelivered : StatusDroppedOff;
        return RefreshItemStatusAsync(ctx, ctx.Message.TripId, target, nameof(TripDropCompletedIntegrationEvent));
    }

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => RefreshItemStatusAsync(ctx, ctx.Message.TripId, StatusDelivered, nameof(TripCompletedIntegrationEvent));

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => RefreshItemStatusAsync(ctx, ctx.Message.TripId, StatusUnbound, nameof(TripFailedIntegrationEvent));

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => RefreshItemStatusAsync(ctx, ctx.Message.TripId, StatusUnbound, nameof(TripCancelledIntegrationEvent));

    private async Task RefreshItemStatusAsync<TEvent>(
        ConsumeContext<TEvent> ctx, Guid tripId, string newStatus, string eventTypeName)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = eventTypeName,
            ["TripId"] = tripId,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, eventTypeName);
            return;
        }

        try
        {
            var updatedCount = await _store.UpdateItemStatusForTripAsync(
                Name, evt.EventId, tripId, newStatus, evt.OccurredOn, ct);

            _metrics.RecordProjected(Name, eventTypeName);
            _metrics.RecordLag(Name, evt.OccurredOn);
            _logger.LogInformation(
                "Trip {TripId} {EventType} → ItemStatus={NewStatus} ({UpdatedCount} rows)",
                tripId, eventTypeName, newStatus, updatedCount);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient projection failure — will retry");
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordPermanentFailure(Name, eventTypeName);
            _logger.LogError(ex,
                "Permanent projection failure for {EventType} {EventId} — event dropped",
                eventTypeName, evt.EventId);
        }
    }

    private static bool IsTransient(Exception ex) => ex is
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException or
        TimeoutException or
        TaskCanceledException;
}
