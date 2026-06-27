using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.UnassignItemsFromTrip;

public class UnassignItemsFromTripCommandHandler : ICommandHandler<UnassignItemsFromTripCommand, int>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<UnassignItemsFromTripCommandHandler> _logger;

    public UnassignItemsFromTripCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<UnassignItemsFromTripCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(UnassignItemsFromTripCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<int>.Failure($"Order {request.OrderId} not found.");

        var released = order.UnassignItemsFromTrip(request.TripId);
        if (released > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "[UnassignItemsFromTrip] Trip {TripId} released {Count} items on Order {OrderId}",
                request.TripId, released, request.OrderId);
        }
        return Result<int>.Success(released);
    }
}
