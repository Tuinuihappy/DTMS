using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RecomputeOrderStatus;

public class RecomputeOrderStatusCommandHandler : ICommandHandler<RecomputeOrderStatusCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<RecomputeOrderStatusCommandHandler> _logger;

    public RecomputeOrderStatusCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<RecomputeOrderStatusCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(RecomputeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result.Failure($"Order {request.OrderId} not found.");

        var before = order.Status;
        order.RecomputeStatusFromItems();
        if (order.Status == before) return Result.Success();   // nothing to persist

        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[Recompute] Order {OrderId}: {Before} → {After}",
            request.OrderId, before, order.Status);
        return Result.Success();
    }
}
