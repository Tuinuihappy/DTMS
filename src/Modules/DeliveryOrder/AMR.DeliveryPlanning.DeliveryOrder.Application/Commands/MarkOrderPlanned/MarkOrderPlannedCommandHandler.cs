using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanned;

public class MarkOrderPlannedCommandHandler : ICommandHandler<MarkOrderPlannedCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<MarkOrderPlannedCommandHandler> _logger;

    public MarkOrderPlannedCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<MarkOrderPlannedCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkOrderPlannedCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.MarkPlanned();
            await _repository.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[MarkOrderPlanned] {OrderId}: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
    }
}
