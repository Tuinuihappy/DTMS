using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Hubs;

/// <summary>
/// Trip-scoped realtime updates. Browser invokes <see cref="Subscribe"/>
/// when opening a Trip detail drawer; the projector for Trip status
/// history (P1) pushes timeline + status-change events to this group.
///
/// <para>Backend Phase 2 — also exposes <see cref="SubscribeList"/> for
/// the dispatcher's cross-trip <c>/dispatch/trips</c> table so it
/// receives a refetch hint whenever any trip changes status. Same
/// two-flavour pattern as <c>OrderHub</c>.</para>
/// </summary>
[Authorize]
public sealed class TripHub : Hub<ITripClient>
{
    private const string ListGroup = "trips-list";

    public Task Subscribe(Guid tripId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupKey(tripId));

    public Task Unsubscribe(Guid tripId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupKey(tripId));

    /// <summary>
    /// Cross-trip list subscription. Pushed by TripStatusHistoryProjector
    /// after each durable history append; payload is a refetch hint, not
    /// a full row.
    /// </summary>
    public Task SubscribeList()
        => Groups.AddToGroupAsync(Context.ConnectionId, ListGroup);

    public Task UnsubscribeList()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, ListGroup);

    public static string GroupKey(Guid tripId) => $"trip:{tripId:N}";

    public static string ListGroupKey => ListGroup;
}
