using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
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

        if (request.NewServiceWindow is null)
            return Result<Guid>.Failure("At least one amendment field (NewServiceWindow) must be provided.");

        var originalSnapshot = JsonSerializer.Serialize(new
        {
            ServiceWindowEarliest = order.ServiceWindow.Earliest,
            ServiceWindowLatest = order.ServiceWindow.Latest,
            order.Status
        });

        try
        {
            var newWindow = new ServiceWindow(request.NewServiceWindow.Earliest, request.NewServiceWindow.Latest);
            order.AmendServiceWindow(newWindow, request.Reason);

            await _orderRepo.UpdateAsync(order, cancellationToken);

            var newSnapshot = JsonSerializer.Serialize(new
            {
                ServiceWindowEarliest = order.ServiceWindow.Earliest,
                ServiceWindowLatest = order.ServiceWindow.Latest,
                order.Status
            });

            var amendment = new OrderAmendment(
                order.Id, AmendmentType.SlaChange, request.Reason,
                originalSnapshot, newSnapshot, request.AmendedBy);

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
