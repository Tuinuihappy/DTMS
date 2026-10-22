using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Services;

// Convenience seam over the 3-step envelope dispatch chain
// (route lookup → resolver → vendor dispatcher) so the Planning consumer
// can fire-and-forget by route without composing the primitives itself.
//
// Returns Result.Failure when no template is registered for the (pickup,
// drop) pair so the caller can fall back to the legacy job/task path.
public interface IDispatchOrderTemplateService
{
    Task<Result<DispatchTemplateResult>> DispatchByRouteAsync(
        Guid deliveryOrderId,
        Guid pickupStationId,
        Guid dropStationId,
        string upperKey,
        int attemptNumber = 1,
        Guid? previousAttemptId = null,
        int? priorityOverride = null,
        string? appointVehicleKeyOverride = null,
        string? appointVehicleNameOverride = null,
        string? appointVehicleGroupKeyOverride = null,
        string? appointVehicleGroupNameOverride = null,
        string? appointQueueWaitAreaOverride = null,
        Guid? jobId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The pre-vendor half of <see cref="DispatchByRouteAsync"/> without its
    /// side effects: is there an active OrderTemplate for the route, and does
    /// it resolve? Lets a caller refuse an order up front instead of letting
    /// it fail in Planning. Failure carries the reason only — the caller
    /// knows the route's location codes and names it.
    /// </summary>
    Task<Result> CheckRouteAsync(
        Guid pickupStationId, Guid dropStationId, CancellationToken cancellationToken = default);
}

public sealed record DispatchTemplateResult(
    Guid OrderTemplateId,
    string TemplateName,
    string VendorOrderKey,
    Guid TripId,
    ResolvedOrder Resolved);
