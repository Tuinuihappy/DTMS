namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P1 — abstraction so the OrderStatusHistoryProjector (Application
/// layer, no awareness of SignalR / Microsoft.AspNetCore.SignalR) can push
/// a freshly-projected timeline entry to interested browsers without
/// depending on the API project's hub types.
///
/// The composition root (API project) provides the SignalR implementation
/// that fans the call out to <c>OrderHub</c>'s <c>"order:{id:N}"</c> group.
/// Unit tests register the <see cref="NoopOrderRealtimePublisher"/> default
/// so a missing wire-up never crashes a projector test.
/// </summary>
public interface IOrderRealtimePublisher
{
    /// <summary>
    /// Push a new timeline entry to subscribers of the given order. Should
    /// be invoked AFTER the projection-store write succeeds — never before
    /// (a push without durable storage would be visible-then-gone on reload).
    /// </summary>
    Task PublishTimelineUpdatedAsync(Guid orderId, OrderTimelineEntryDto entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wire shape for the <c>TimelineUpdated</c> SignalR callback. Pure data;
/// the projector + publisher both treat it as a thin DTO that mirrors the
/// row written to <c>order_status_history</c>.
/// </summary>
public sealed record OrderTimelineEntryDto(
    Guid EventId,
    Guid OrderId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

/// <summary>
/// Default null implementation — keeps the projector working when no
/// realtime layer is wired (tests, alternative compositions). Registered by
/// the Application's own DI extension; overridden by the API project's
/// SignalR-backed implementation.
/// </summary>
public sealed class NoopOrderRealtimePublisher : IOrderRealtimePublisher
{
    public Task PublishTimelineUpdatedAsync(
        Guid orderId, OrderTimelineEntryDto entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
