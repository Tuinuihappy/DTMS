using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;

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
            request.PickupStationId, request.DropStationId,
            request.PickupWmsLocationId, request.DropWmsLocationId,
            request.Reason);

        if (marked > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
            // Log whichever pair the caller passed — caller knows the order's mode.
            var groupLabel = request.PickupStationId.HasValue && request.PickupStationId.Value != Guid.Empty
                ? $"station {request.PickupStationId} → {request.DropStationId}"
                : $"WMS location {request.PickupWmsLocationId} → {request.DropWmsLocationId}";
            _logger.LogWarning(
                "[GroupDispatchFailed] Order {OrderId} ({Group}): {Count} items marked Failed — {Reason}",
                request.OrderId, groupLabel, marked, request.Reason);
        }
        return Result<int>.Success(marked);
    }
}
