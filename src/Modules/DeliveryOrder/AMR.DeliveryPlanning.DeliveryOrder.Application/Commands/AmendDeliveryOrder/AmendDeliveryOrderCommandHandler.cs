using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public class AmendDeliveryOrderCommandHandler : ICommandHandler<AmendDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _orderRepo;
    private readonly IOrderAmendmentRepository _amendmentRepo;

    public AmendDeliveryOrderCommandHandler(
        IDeliveryOrderRepository orderRepo,
        IOrderAmendmentRepository amendmentRepo)
    {
        _orderRepo = orderRepo;
        _amendmentRepo = amendmentRepo;
    }

    public async Task<Result<Guid>> Handle(AmendDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<Guid>.Failure($"Order {request.OrderId} not found.");

        var originalSnapshot = JsonSerializer.Serialize(new
        {
            order.Priority,
            order.SLA,
            order.Status
        });

        try
        {
            if (request.NewPriority.HasValue)
                order.AmendPriority(request.NewPriority.Value, request.Reason);

            if (request.NewSla.HasValue)
                order.AmendSla(request.NewSla.Value, request.Reason);

            await _orderRepo.UpdateAsync(order, cancellationToken);
            await _orderRepo.SaveChangesAsync(cancellationToken);

            var newSnapshot = JsonSerializer.Serialize(new
            {
                order.Priority,
                order.SLA,
                order.Status
            });

            var amendmentType = request.NewPriority.HasValue ? AmendmentType.PriorityChange : AmendmentType.SlaChange;
            var amendment = new OrderAmendment(
                order.Id, amendmentType, request.Reason,
                originalSnapshot, newSnapshot, request.AmendedBy);

            await _amendmentRepo.AddAsync(amendment, cancellationToken);
            await _amendmentRepo.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(amendment.Id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }
}
