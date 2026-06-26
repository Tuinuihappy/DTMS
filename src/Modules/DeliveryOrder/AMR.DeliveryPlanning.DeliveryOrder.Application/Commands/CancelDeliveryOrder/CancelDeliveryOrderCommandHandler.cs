using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;

public class CancelDeliveryOrderCommandHandler : ICommandHandler<CancelDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<CancelDeliveryOrderCommandHandler> _logger;

    public CancelDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<CancelDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(CancelDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Cancel(request.Reason);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Cancel] Order {OrderId} cancelled. Reason: {Reason}.", request.OrderId, request.Reason);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Cancel] Order {OrderId} cannot be cancelled: {Error}.", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Cancel] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }
}
