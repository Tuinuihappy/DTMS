using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkOrderDispatched;

public class MarkOrderDispatchedCommandHandler : ICommandHandler<MarkOrderDispatchedCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<MarkOrderDispatchedCommandHandler> _logger;

    public MarkOrderDispatchedCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<MarkOrderDispatchedCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkOrderDispatchedCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.MarkDispatched();
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[MarkOrderDispatched] Order {OrderId} → Dispatched", request.OrderId);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[MarkOrderDispatched] {OrderId}: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
    }
}
