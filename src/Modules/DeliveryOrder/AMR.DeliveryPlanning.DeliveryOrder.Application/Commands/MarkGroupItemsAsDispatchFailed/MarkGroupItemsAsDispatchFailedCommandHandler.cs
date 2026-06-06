using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;

public class MarkGroupItemsAsDispatchFailedCommandHandler : ICommandHandler<MarkGroupItemsAsDispatchFailedCommand, int>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<MarkGroupItemsAsDispatchFailedCommandHandler> _logger;

    public MarkGroupItemsAsDispatchFailedCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<MarkGroupItemsAsDispatchFailedCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(MarkGroupItemsAsDispatchFailedCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result<int>.Failure($"Order {request.OrderId} not found.");

        var marked = order.MarkGroupItemsAsDispatchFailed(
            request.PickupStationId, request.DropStationId, request.Reason);

        if (marked > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "[GroupDispatchFailed] Order {OrderId} ({Pickup} → {Drop}): {Count} items marked Failed — {Reason}",
                request.OrderId, request.PickupStationId, request.DropStationId, marked, request.Reason);
        }
        return Result<int>.Success(marked);
    }
}
