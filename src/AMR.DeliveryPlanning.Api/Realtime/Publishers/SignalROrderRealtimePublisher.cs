using AMR.DeliveryPlanning.Api.Realtime.Hubs;
using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Publishers;

/// <summary>
/// Composition-root realisation of <see cref="IOrderRealtimePublisher"/>.
/// Forwards the projector's timeline entry to subscribers of the
/// <c>"order:{id:N}"</c> SignalR group via <see cref="OrderHub"/>.
///
/// Failures (e.g. the SignalR backplane is briefly unreachable) are
/// swallowed + logged. Durable visibility lives in
/// <c>order_status_history</c>; missing a live push only delays a UI
/// update by one polling tick (browsers always render initial state from
/// REST then patch in deltas).
/// </summary>
public sealed class SignalROrderRealtimePublisher : IOrderRealtimePublisher
{
    private readonly IHubContext<OrderHub, IOrderClient> _hub;
    private readonly ILogger<SignalROrderRealtimePublisher> _logger;

    public SignalROrderRealtimePublisher(
        IHubContext<OrderHub, IOrderClient> hub,
        ILogger<SignalROrderRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishTimelineUpdatedAsync(
        Guid orderId,
        OrderTimelineEntryDto entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(OrderHub.GroupKey(orderId))
                .TimelineUpdated(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push TimelineUpdated for Order {OrderId} — UI will catch up on next REST refresh",
                orderId);
        }
    }

    public async Task PublishActivityUpdatedAsync(
        Guid orderId,
        OrderActivityEntryDto entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(OrderHub.GroupKey(orderId))
                .ActivityUpdated(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push ActivityUpdated for Order {OrderId} — UI will catch up on next REST refresh",
                orderId);
        }
    }
}
