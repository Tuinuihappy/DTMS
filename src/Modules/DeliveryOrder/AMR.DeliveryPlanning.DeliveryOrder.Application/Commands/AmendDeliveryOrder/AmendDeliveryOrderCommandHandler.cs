using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;

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

        if (!request.NewPriority.HasValue && !request.NewSla.HasValue)
            return Result<Guid>.Failure("At least one amendment field (NewPriority or NewSla) must be provided.");

        try
        {
            if (request.NewPriority.HasValue)
                order.AmendPriority(request.NewPriority.Value, request.Reason);

            if (request.NewSla.HasValue)
                order.AmendSla(request.NewSla.Value, request.Reason);

            var amendmentType = (request.NewPriority.HasValue, request.NewSla.HasValue) switch
            {
                (true, true)  => AmendmentType.CombinedChange,
                (true, false) => AmendmentType.PriorityChange,
                _             => AmendmentType.SlaChange
            };

            await _orderRepo.UpdateAsync(order, cancellationToken);

            var newSnapshot = JsonSerializer.Serialize(new { order.Priority, order.SLA, order.Status });
            var amendment = new OrderAmendment(order.Id, amendmentType, request.Reason, originalSnapshot, newSnapshot, request.AmendedBy);

            await _amendmentRepo.AddAsync(amendment, cancellationToken);
            await _orderRepo.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(amendment.Id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Guid>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
