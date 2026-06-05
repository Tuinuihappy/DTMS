using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Module-agnostic seam used by the trip retry handler to invoke the
/// envelope-dispatch pipeline (route → template → vendor). Implementation
/// lives at the composition root so Dispatch.Application stays free of
/// the Planning.Application reference that the underlying service needs.
/// </summary>
public interface ITripRetryDispatcher
{
    Task<Result<Guid>> ReissueAsync(
        Guid deliveryOrderId,
        Guid pickupStationId,
        Guid dropStationId,
        string newUpperKey,
        int attemptNumber,
        Guid previousAttemptId,
        CancellationToken cancellationToken = default);
}
