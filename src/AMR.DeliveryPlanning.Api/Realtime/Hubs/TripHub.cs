using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Hubs;

/// <summary>
/// Trip-scoped realtime updates. Browser invokes <see cref="Subscribe"/>
/// when opening a Trip detail drawer; the projector for Trip status
/// history (P1) pushes timeline + status-change events to this group.
/// </summary>
[Authorize]
public sealed class TripHub : Hub<ITripClient>
{
    public Task Subscribe(Guid tripId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupKey(tripId));

    public Task Unsubscribe(Guid tripId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupKey(tripId));

    public static string GroupKey(Guid tripId) => $"trip:{tripId:N}";
}
