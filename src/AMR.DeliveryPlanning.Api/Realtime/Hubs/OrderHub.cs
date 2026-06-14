using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Hubs;

/// <summary>
/// Order-scoped realtime updates. Browser invokes <see cref="Subscribe"/>
/// when it opens an order's detail drawer — pushed updates arrive on
/// <see cref="IOrderClient"/> until the connection closes or
/// <see cref="Unsubscribe"/> is called.
///
/// Hub method body intentionally does NO database work: pushes the
/// connection into a group and returns. Initial state is fetched via
/// REST snapshot (P1) so the hot path stays sub-millisecond. See
/// docs/event-projection-implementation-plan.md §"Hub Design Patterns".
/// </summary>
[Authorize]
public sealed class OrderHub : Hub<IOrderClient>
{
    public Task Subscribe(Guid orderId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupKey(orderId));

    public Task Unsubscribe(Guid orderId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupKey(orderId));

    /// <summary>
    /// Group identifier convention. <c>"N"</c> formatter drops the dashes
    /// so the key is 32 chars instead of 36 — small wire savings across
    /// many groups, no semantic change.
    /// </summary>
    public static string GroupKey(Guid orderId) => $"order:{orderId:N}";
}
