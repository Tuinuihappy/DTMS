using DTMS.Api.Realtime.Hubs;
using DTMS.Api.Realtime.Hubs.Clients;
using DTMS.Dispatch.Application.Projections;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Publishers;

public sealed class SignalRTripRealtimePublisher : ITripRealtimePublisher
{
    private readonly IHubContext<TripHub, ITripClient> _hub;
    private readonly ILogger<SignalRTripRealtimePublisher> _logger;

    public SignalRTripRealtimePublisher(
        IHubContext<TripHub, ITripClient> hub,
        ILogger<SignalRTripRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishTimelineUpdatedAsync(
        Guid tripId,
        TripTimelineEntryDto entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(TripHub.GroupKey(tripId))
                .TimelineUpdated(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push TimelineUpdated for Trip {TripId} — UI will catch up on next REST refresh",
                tripId);
        }
    }

    public async Task PublishMissionUpdatedAsync(
        Guid tripId,
        TripMissionEventDto entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(TripHub.GroupKey(tripId))
                .MissionUpdated(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push MissionUpdated for Trip {TripId} (mission {MissionKey} → {State}) — UI will catch up on next REST refresh",
                tripId, entry.MissionKey, entry.State);
        }
    }

    public async Task PublishTripListChangedAsync(
        Guid tripId,
        string toStatus,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Push minimal hint to the cross-trip list group — the
            // /dispatch/trips page treats this as a refetch trigger.
            await _hub.Clients
                .Group(TripHub.ListGroupKey)
                .ListItemUpdated(new { tripId, toStatus });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push ListItemUpdated hint for Trip {TripId} — UI will catch up on next REST refresh",
                tripId);
        }
    }
}
