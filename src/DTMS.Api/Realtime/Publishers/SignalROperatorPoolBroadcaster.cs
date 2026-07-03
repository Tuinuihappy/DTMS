using DTMS.Api.Infrastructure.Metrics;
using DTMS.Api.Realtime.Hubs;
using DTMS.Api.Realtime.Hubs.Clients;
using DTMS.Transport.Manual.Application.Queries.GetPoolTrips;
using DTMS.Transport.Manual.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Publishers;

/// <summary>
/// WMS PR-4b (PR-D) — Broadcasts pool events to every connected operator
/// PWA via <see cref="OperatorPoolHub"/>. Fire-and-forget: any exception
/// is logged at warn level and swallowed — the caller's DB save has
/// already committed, and clients recover by re-fetching the REST list
/// on their next reconnect.
///
/// The universal group <c>OperatorPoolHub.PoolGroup</c> is joined
/// automatically at connection time (see <c>OnConnectedAsync</c>) so
/// there's no per-client filter or tenant scoping here.
/// </summary>
public sealed class SignalROperatorPoolBroadcaster : IOperatorPoolBroadcaster
{
    // Broadcast label constants — kept in sync with the taxonomy on the
    // dtms.pool.broadcast.total counter (event, outcome).
    private const string EventAdded = "added";
    private const string EventClaimed = "claimed";
    private const string EventRemoved = "removed";
    private const string OutcomeSent = "sent";
    private const string OutcomeFailed = "failed";

    private readonly IHubContext<OperatorPoolHub, IOperatorPoolClient> _hub;
    private readonly PoolMetrics _metrics;
    private readonly ILogger<SignalROperatorPoolBroadcaster> _logger;

    public SignalROperatorPoolBroadcaster(
        IHubContext<OperatorPoolHub, IOperatorPoolClient> hub,
        PoolMetrics metrics,
        ILogger<SignalROperatorPoolBroadcaster> logger)
    {
        _hub = hub;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task BroadcastAddedAsync(PoolTripDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(OperatorPoolHub.PoolGroup).PoolTripAdded(dto);
            _metrics.RecordBroadcast(EventAdded, OutcomeSent);
            _logger.LogInformation(
                "[PoolBroadcast] Added Trip {TripId} (order {OrderRef}) → operator-pool",
                dto.TripId, dto.OrderRef);
        }
        catch (Exception ex)
        {
            _metrics.RecordBroadcast(EventAdded, OutcomeFailed);
            _logger.LogWarning(ex,
                "[PoolBroadcast] Failed to push PoolTripAdded for Trip {TripId} — clients recover via REST refetch",
                dto.TripId);
        }
    }

    public async Task BroadcastClaimedAsync(
        Guid tripId, Guid operatorId, string operatorName,
        DateTime claimedAt, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(OperatorPoolHub.PoolGroup).PoolTripClaimed(new
            {
                tripId,
                claimedByOperatorId = operatorId,
                claimedByName = operatorName,
                claimedAt,
            });
            _metrics.RecordBroadcast(EventClaimed, OutcomeSent);
            _logger.LogInformation(
                "[PoolBroadcast] Claimed Trip {TripId} by operator {OperatorId} ({OperatorName}) → operator-pool",
                tripId, operatorId, operatorName);
        }
        catch (Exception ex)
        {
            _metrics.RecordBroadcast(EventClaimed, OutcomeFailed);
            _logger.LogWarning(ex,
                "[PoolBroadcast] Failed to push PoolTripClaimed for Trip {TripId}",
                tripId);
        }
    }

    public async Task BroadcastRemovedAsync(Guid tripId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(OperatorPoolHub.PoolGroup).PoolTripRemoved(new { tripId, reason });
            _metrics.RecordBroadcast(EventRemoved, OutcomeSent);
            _logger.LogInformation(
                "[PoolBroadcast] Removed Trip {TripId} ({Reason}) → operator-pool",
                tripId, reason);
        }
        catch (Exception ex)
        {
            _metrics.RecordBroadcast(EventRemoved, OutcomeFailed);
            _logger.LogWarning(ex,
                "[PoolBroadcast] Failed to push PoolTripRemoved for Trip {TripId}",
                tripId);
        }
    }
}
