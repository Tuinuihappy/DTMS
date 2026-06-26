using DTMS.Fleet.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Fleet.Application.Projections;

/// <summary>
/// Phase P3.2 — Materializes per-vehicle state transitions from
/// <c>VehicleStateChangedIntegrationEvent</c>. Used by the operator
/// robot drilldown + as the data source for backfilling
/// FleetUtilizationHourly buckets when the snapshot service hasn't
/// run for a while.
/// </summary>
public class VehicleStateHistoryProjector : IConsumer<VehicleStateChangedIntegrationEvent>
{
    public const string Name = nameof(VehicleStateHistoryProjector);

    private readonly IVehicleStateHistoryProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<VehicleStateHistoryProjector> _logger;

    public VehicleStateHistoryProjector(
        IVehicleStateHistoryProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<VehicleStateHistoryProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VehicleStateChangedIntegrationEvent> ctx)
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["VehicleId"] = evt.VehicleId,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, nameof(VehicleStateChangedIntegrationEvent));
            return;
        }

        try
        {
            var latest = await _store.GetLatestForVehicleAsync(evt.VehicleId, ct);

            // Out-of-order guard — drop strictly older events to preserve
            // the state-chain integrity (same rule as P1 status history).
            if (latest is { } prev && evt.OccurredOn < prev.OccurredAt)
            {
                _metrics.RecordPermanentFailure(Name, nameof(VehicleStateChangedIntegrationEvent));
                _logger.LogWarning(
                    "Out-of-order vehicle state event {EventId} for {VehicleId} skipped " +
                    "(event time {EventTime:O} < latest {LatestTime:O})",
                    evt.EventId, evt.VehicleId, evt.OccurredOn, prev.OccurredAt);
                return;
            }

            await _store.AppendAsync(
                Name, evt.EventId, evt.VehicleId,
                fromState: latest?.ToState,
                toState: evt.State,
                batteryLevel: evt.BatteryLevel,
                currentNodeId: evt.CurrentNodeId,
                occurredAt: evt.OccurredOn,
                ct);

            _metrics.RecordProjected(Name, nameof(VehicleStateChangedIntegrationEvent));
            _metrics.RecordLag(Name, evt.OccurredOn);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient projection failure — will retry");
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordPermanentFailure(Name, nameof(VehicleStateChangedIntegrationEvent));
            _logger.LogError(ex,
                "Permanent projection failure for VehicleStateChanged {EventId} — event dropped",
                evt.EventId);
        }
    }

    private static bool IsTransient(Exception ex) => ex is
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException or
        TimeoutException or
        TaskCanceledException;
}
