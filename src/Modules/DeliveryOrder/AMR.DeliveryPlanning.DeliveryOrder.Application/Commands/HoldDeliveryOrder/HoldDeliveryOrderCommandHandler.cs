using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.HoldDeliveryOrder;

public class HoldDeliveryOrderCommandHandler : ICommandHandler<HoldDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ILogger<HoldDeliveryOrderCommandHandler> _logger;

    public HoldDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ILogger<HoldDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task<Result> Handle(HoldDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Hold(request.Reason);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderHeld",
                $"Order '{order.OrderRef}' held by {request.HeldBy ?? "system"}. Reason: {request.Reason}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Hold] Order {OrderId} '{OrderRef}' held. Reason: {Reason}.",
                order.Id, order.OrderRef, request.Reason);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Hold] Order {OrderId} hold failed: {Error}.", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Hold] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }
}
