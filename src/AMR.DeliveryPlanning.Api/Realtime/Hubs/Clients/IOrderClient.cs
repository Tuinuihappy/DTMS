namespace AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;

/// <summary>
/// Typed SignalR client for <see cref="OrderHub"/>. Each method here is a
/// callback the server invokes on subscribed browsers — the corresponding
/// frontend hook (<c>useHubSubscription</c>) registers handlers by these
/// exact names.
///
/// Payload shape is intentionally loose (<c>object</c>) at this layer so
/// the projector that pushes the event chooses the DTO. P1 (Status History)
/// will tighten this to <c>StatusTimelineEntryDto</c> when the schema is
/// finalised.
/// </summary>
public interface IOrderClient
{
    /// <summary>
    /// One new entry was appended to an order's status timeline. Payload
    /// shape = <c>{ orderId, fromStatus, toStatus, occurredAt, triggeredBy, reason }</c>.
    /// </summary>
    Task TimelineUpdated(object entry);

    /// <summary>
    /// The order's overall <c>OrderStatus</c> changed. Used by list views
    /// + badges. Payload = <c>{ orderId, fromStatus, toStatus, occurredAt }</c>.
    /// </summary>
    Task StatusChanged(object change);

    /// <summary>
    /// Phase P2 — unified activity timeline entry (status / amendment /
    /// trip event / POD / OMS notification). Subscribers can filter by
    /// <c>category</c> on the client.
    /// </summary>
    Task ActivityUpdated(object entry);
}
