using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Api.Adapters;

/// <summary>
/// Composition-root bridge: lets Dispatch.Application's
/// ReissueTripCommand reach Planning.Application's
/// IDispatchOrderTemplateService without Dispatch.Application taking a
/// direct dependency on Planning.Application.
/// </summary>
internal sealed class PlanningTripRetryDispatcher : ITripRetryDispatcher
{
    private readonly IDispatchOrderTemplateService _dispatchService;

    public PlanningTripRetryDispatcher(IDispatchOrderTemplateService dispatchService)
    {
        _dispatchService = dispatchService;
    }

    public async Task<Result<Guid>> ReissueAsync(
        Guid deliveryOrderId,
        Guid pickupStationId,
        Guid dropStationId,
        string newUpperKey,
        int attemptNumber,
        Guid previousAttemptId,
        CancellationToken cancellationToken = default)
    {
        var result = await _dispatchService.DispatchByRouteAsync(
            deliveryOrderId,
            pickupStationId,
            dropStationId,
            newUpperKey,
            attemptNumber: attemptNumber,
            previousAttemptId: previousAttemptId,
            cancellationToken: cancellationToken);

        return result.IsSuccess
            ? Result<Guid>.Success(result.Value.TripId)
            : Result<Guid>.Failure(result.Error!);
    }
}
