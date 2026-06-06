namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Minimal cross-module seam letting Dispatch handlers see the parent
/// DeliveryOrder's lifecycle status without dragging the entire
/// DeliveryOrder model in. The composition root supplies an adapter
/// that proxies to DeliveryOrder.Application's query stack so we
/// don't introduce a Dispatch.Application → DeliveryOrder.* reference
/// (which would mirror — and worsen — the existing one-way coupling).
/// </summary>
public interface IDeliveryOrderStatusReader
{
    /// <summary>
    /// Returns the order's current Status as a string (e.g. "Confirmed",
    /// "Cancelled", "Failed"). Returns null if the order doesn't exist —
    /// callers decide whether that's a hard failure or a soft skip.
    /// </summary>
    Task<string?> GetStatusAsync(Guid orderId, CancellationToken cancellationToken = default);
}
