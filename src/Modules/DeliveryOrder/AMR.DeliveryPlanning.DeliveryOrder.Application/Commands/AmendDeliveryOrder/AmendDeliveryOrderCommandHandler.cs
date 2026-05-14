using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
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

        var amendedBy = string.IsNullOrWhiteSpace(request.AmendedBy) ? null : request.AmendedBy.Trim();

        if (request.NewRequestedDeliveryDate is null)
            return Result<Guid>.Failure("At least one amendment field (NewRequestedDeliveryDate) must be provided.");

        var originalSnapshot = JsonSerializer.Serialize(new
        {
            RequestedDeliveryDate = order.RequestedDeliveryDate,
            OrderStatus = order.Status
        });

        try
        {
            order.AmendRequestedDeliveryDate(request.NewRequestedDeliveryDate, request.Reason);

            await _orderRepo.UpdateAsync(order, cancellationToken);

            var newSnapshot = JsonSerializer.Serialize(new
            {
                RequestedDeliveryDate = order.RequestedDeliveryDate,
                OrderStatus = order.Status
            });

            var amendment = new OrderAmendment(
                order.Id, AmendmentType.RequestedTimeChange, request.Reason,
                originalSnapshot, newSnapshot, amendedBy);

            await _amendmentRepo.AddAsync(amendment, cancellationToken);
            await _orderRepo.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Amend] Order {OrderId} amended ({AmendmentType}) by {AmendedBy}.",
                request.OrderId, amendment.Type, amendedBy ?? "system");

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
