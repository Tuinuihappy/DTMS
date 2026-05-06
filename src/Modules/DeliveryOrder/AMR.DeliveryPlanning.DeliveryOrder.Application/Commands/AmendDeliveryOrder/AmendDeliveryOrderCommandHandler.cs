using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public class AmendDeliveryOrderCommandHandler : ICommandHandler<AmendDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _orderRepo;
    private readonly IOrderAmendmentRepository _amendmentRepo;
    private readonly ILogger<AmendDeliveryOrderCommandHandler> _logger;

    public AmendDeliveryOrderCommandHandler(
        IDeliveryOrderRepository orderRepo,
        IOrderAmendmentRepository amendmentRepo,
        ILogger<AmendDeliveryOrderCommandHandler> logger)
    {
        _orderRepo = orderRepo;
        _amendmentRepo = amendmentRepo;
        _logger = logger;
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
                order.Id, AmendmentType.ServiceWindowChange, request.Reason,
                originalSnapshot, newSnapshot, request.AmendedBy);

            await _amendmentRepo.AddAsync(amendment, cancellationToken);
            await _orderRepo.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Amend] Order {OrderId} amended ({AmendmentType}) by {AmendedBy}.",
                request.OrderId, amendment.Type, request.AmendedBy ?? "system");

            return Result<Guid>.Success(amendment.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Amend] Order {OrderId} amendment failed: {Error}.", request.OrderId, ex.Message);
            return Result<Guid>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Amend] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<Guid>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
