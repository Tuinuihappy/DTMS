using DTMS.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Hubs;

/// <summary>
/// Facility-scoped robot telemetry. Each facility floor is a separate
/// group so an operator watching warehouse A doesn't pay for warehouse B's
/// position deltas. Positions are throttled + batched by the
/// <c>FleetPositionThrottler</c> (1 push/sec per floor) regardless of
/// upstream webhook rate.
/// </summary>
[Authorize]
public sealed class FleetHub : Hub<IFleetClient>
{
    public Task SubscribeFloor(Guid facilityId)
        => Groups.AddToGroupAsync(Context.ConnectionId, FloorGroupKey(facilityId));

    public Task UnsubscribeFloor(Guid facilityId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, FloorGroupKey(facilityId));

    public static string FloorGroupKey(Guid facilityId) => $"floor:{facilityId:N}";
}
