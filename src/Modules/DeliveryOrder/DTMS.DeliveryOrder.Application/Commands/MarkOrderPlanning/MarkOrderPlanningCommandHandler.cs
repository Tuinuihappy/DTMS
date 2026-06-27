using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanning;

public class MarkOrderPlanningCommandHandler : ICommandHandler<MarkOrderPlanningCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<MarkOrderPlanningCommandHandler> _logger;

    public MarkOrderPlanningCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<MarkOrderPlanningCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkOrderPlanningCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.MarkPlanning();
            await _repository.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[MarkOrderPlanning] {OrderId}: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
    }
}
