using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;

public class CancelDeliveryOrderCommandHandler : ICommandHandler<CancelDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;

    public CancelDeliveryOrderCommandHandler(IDeliveryOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(CancelDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
        {
            return Result.Failure($"Order {request.OrderId} not found.");
        }

        try
        {
            order.Cancel(request.Reason);
            await _repository.UpdateAsync(order, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
