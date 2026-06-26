using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AssignItemsToTrip;

public class AssignItemsToTripCommandHandler : ICommandHandler<AssignItemsToTripCommand, int>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<AssignItemsToTripCommandHandler> _logger;

    public AssignItemsToTripCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<AssignItemsToTripCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(AssignItemsToTripCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<int>.Failure($"Order {request.OrderId} not found.");

        var bound = order.AssignItemsToTrip(
            request.TripId,
            request.AttemptNumber,
            request.PickupStationId,
            request.DropStationId,
            request.PickupWarehouseId,
            request.DropWarehouseId);

        if (bound == 0)
        {
            // Surface but don't fail — the trip is already dispatched on
            // the vendor side. Likely cause: items grouped at the wrong
            // station pair or the order shape changed mid-flight.
            _logger.LogWarning(
                "[AssignItemsToTrip] Trip {TripId} bound 0 items on Order {OrderId} ({Pickup} → {Drop}). " +
                "Multi-group completion will treat this trip as routeless.",
                request.TripId, request.OrderId, request.PickupStationId, request.DropStationId);
            return Result<int>.Success(0);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "[AssignItemsToTrip] ✓ Trip {TripId} attempt {Attempt} bound {Count} items on Order {OrderId}",
            request.TripId, request.AttemptNumber, bound, request.OrderId);
        return Result<int>.Success(bound);
    }
}
