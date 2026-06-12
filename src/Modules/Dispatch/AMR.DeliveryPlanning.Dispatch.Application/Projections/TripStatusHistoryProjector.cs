using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Phase P1 — Materializes Trip status transitions into the
/// dispatch.TripStatusHistory read model. Subscribes to every Dispatch
/// integration event that implies a status change.
///
/// Covered: InProgress (TripStarted), Paused, Completed, Failed,
/// Cancelled. The initial Created state is seeded by the backfill SQL
/// because Trip.CreateForEnvelope doesn't emit a domain event (the row
/// itself IS the event).
///
/// Idempotent + out-of-order safe (same rules as the Order/Job
/// projectors).
/// </summary>
public class TripStatusHistoryProjector :
    IConsumer<TripStartedIntegrationEvent>,
    IConsumer<TripPausedIntegrationEventV1>,
    IConsumer<TripResumedIntegrationEventV1>,
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>
{
    public const string Name = nameof(TripStatusHistoryProjector);

    private readonly ITripStatusHistoryProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<TripStatusHistoryProjector> _logger;

    public TripStatusHistoryProjector(
        ITripStatusHistoryProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<TripStatusHistoryProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.TripId, ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            "InProgress",
            reason: ctx.Message.VehicleId == Guid.Empty ? null : $"vehicle={ctx.Message.VehicleId}");

    public Task Consume(ConsumeContext<TripPausedIntegrationEventV1> ctx)
        // Paused payload doesn't carry order/job ids — projector reads them
        // from the latest history row via the store.
        => Project(ctx, ctx.Message.TripId, deliveryOrderId: null, jobId: null,
            "Paused", reason: null);

    public Task Consume(ConsumeContext<TripResumedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.TripId, deliveryOrderId: null, jobId: null,
            "InProgress", reason: "Resumed from pause");

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.TripId, ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            "Completed",
            reason: string.IsNullOrWhiteSpace(ctx.Message.VendorUpperKey) ? null : $"upper={ctx.Message.VendorUpperKey}");

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.TripId, ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            "Failed", ctx.Message.Reason);

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.TripId, ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            "Cancelled", ctx.Message.Reason);

    private async Task Project<TEvent>(
        ConsumeContext<TEvent> ctx, Guid tripId, Guid? deliveryOrderId, Guid? jobId,
        string toStatus, string? reason)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
            ["TripId"] = tripId,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            var latest = await _store.GetLatestForTripAsync(tripId, ct);

            if (latest is { } prev && evt.OccurredOn < prev.OccurredAt)
            {
                _metrics.RecordPermanentFailure(Name, typeof(TEvent).Name);
                _logger.LogWarning(
                    "Out-of-order event {EventId} for Trip {TripId} skipped " +
                    "(event time {EventTime:O} < latest recorded {LatestTime:O})",
                    evt.EventId, tripId, evt.OccurredOn, prev.OccurredAt);
                return;
            }

            var fromStatus = latest?.ToStatus;
            // Carry forward order/job ids from the latest row when the
            // current event doesn't include them (pause/resume).
            var effectiveOrderId = deliveryOrderId ?? latest?.DeliveryOrderId;
            var effectiveJobId = jobId ?? latest?.JobId;

            await _store.AppendAsync(
                Name, evt.EventId, tripId,
                effectiveOrderId, effectiveJobId,
                fromStatus, toStatus, evt.OccurredOn, reason, ct);

            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            _logger.LogInformation(
                "Projected {EventType} for Trip {TripId}: {From}→{To}",
                typeof(TEvent).Name, tripId, fromStatus ?? "(initial)", toStatus);
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
