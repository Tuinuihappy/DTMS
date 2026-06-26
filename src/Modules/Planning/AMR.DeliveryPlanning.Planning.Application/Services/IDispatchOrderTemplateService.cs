using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Services;

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
}

public sealed record DispatchTemplateResult(
    Guid OrderTemplateId,
    string TemplateName,
    string VendorOrderKey,
    Guid TripId,
    ResolvedOrder Resolved);
