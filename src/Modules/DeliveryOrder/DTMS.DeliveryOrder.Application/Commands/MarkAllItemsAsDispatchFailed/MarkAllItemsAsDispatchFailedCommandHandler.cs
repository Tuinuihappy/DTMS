using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkAllItemsAsDispatchFailed;

public class MarkAllItemsAsDispatchFailedCommandHandler : ICommandHandler<MarkAllItemsAsDispatchFailedCommand, int>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<MarkAllItemsAsDispatchFailedCommandHandler> _logger;

    public MarkAllItemsAsDispatchFailedCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<MarkAllItemsAsDispatchFailedCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(MarkAllItemsAsDispatchFailedCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result<int>.Failure($"Order {request.OrderId} not found.");

        var marked = order.MarkAllPendingItemsAsDispatchFailed(request.Reason);

        if (marked > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "[AllItemsDispatchFailed] Order {OrderId}: {Count} items marked Failed — {Reason}",
                request.OrderId, marked, request.Reason);
        }
        return Result<int>.Success(marked);
    }
}
