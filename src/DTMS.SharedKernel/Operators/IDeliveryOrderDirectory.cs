namespace DTMS.SharedKernel.Operators;

/// <summary>
/// Order-level context the Dispatch Trips list / detail needs to render the
/// "Vehicle / Operator" column for trips that carry no vendor vehicle and no
/// claiming operator. <see cref="TransportMode"/> is the order's requested
/// mode (e.g. "Amr", "Manual", "Fleet") so the UI can interpret the executor
/// label per mode — a robot-mode trip must NOT fall back to the requester.
/// Both fields may be null (order missing, or no requester / mode recorded).
/// </summary>
public sealed record DeliveryOrderTripInfo(string? RequestedBy, string? TransportMode);

/// <summary>
/// Cross-module read port for resolving order-level display context from a
/// <c>DeliveryOrderId</c>. The <c>DeliveryOrder</c> aggregate lives in the
/// DeliveryOrder module; modules that only hold an opaque
/// <c>DeliveryOrderId</c> (e.g. Dispatch, whose <c>Trip.DeliveryOrderId</c>
/// carries no order-level identity) use this port to surface the requester
/// name + transport mode without taking a project reference on DeliveryOrder.
///
/// Mirrors <see cref="IOperatorDirectory"/>: implemented in
/// DeliveryOrder.Infrastructure, wired in ModuleServiceRegistration.
/// </summary>
public interface IDeliveryOrderDirectory
{
    /// <summary>
    /// Returns the order's requester + transport mode, or <c>null</c> when no
    /// order with that Id exists.
    /// </summary>
    Task<DeliveryOrderTripInfo?> GetTripInfoAsync(Guid deliveryOrderId, CancellationToken ct = default);

    /// <summary>
    /// Batch variant for list views (e.g. the Trips queue): resolves many
    /// orders in one round trip to avoid N+1. Ids with no matching order are
    /// simply absent from the returned map.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DeliveryOrderTripInfo>> GetTripInfoAsync(
        IReadOnlyCollection<Guid> deliveryOrderIds, CancellationToken ct = default);
}
