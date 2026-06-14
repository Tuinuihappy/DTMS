using AMR.DeliveryPlanning.Api.Realtime.Hubs;
using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Publishers;

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
}
