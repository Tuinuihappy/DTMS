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
/// <para>Phase P4 — also exposes <see cref="SubscribeList"/> for the
/// cross-order <c>/delivery-orders/list</c> page so it receives a hint
/// (debounce-refetch) whenever any row in the OrderListView projection
/// changes. Same two-flavour pattern as <c>JobHub</c>.</para>
///
/// Hub method body intentionally does NO database work: pushes the
/// connection into a group and returns. Initial state is fetched via
/// REST snapshot (P1) so the hot path stays sub-millisecond. See
/// docs/event-projection-implementation-plan.md §"Hub Design Patterns".
/// </summary>
[Authorize]
public sealed class OrderHub : Hub<IOrderClient>
{
    private const string ListGroup = "orders-list";

    public Task Subscribe(Guid orderId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupKey(orderId));

    public Task Unsubscribe(Guid orderId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupKey(orderId));

    /// <summary>
    /// Cross-order list subscription. Pushed by OrderListViewProjector
    /// after each row upsert; payload is a refetch hint, not a full row.
    /// </summary>
    public Task SubscribeList()
        => Groups.AddToGroupAsync(Context.ConnectionId, ListGroup);

    public Task UnsubscribeList()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, ListGroup);

    /// <summary>
    /// Group identifier convention. <c>"N"</c> formatter drops the dashes
    /// so the key is 32 chars instead of 36 — small wire savings across
    /// many groups, no semantic change.
    /// </summary>
    public static string GroupKey(Guid orderId) => $"order:{orderId:N}";

    public static string ListGroupKey => ListGroup;
}
