using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RejectDeliveryOrder;

public class RejectDeliveryOrderCommandHandler : ICommandHandler<RejectDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ILogger<RejectDeliveryOrderCommandHandler> _logger;

    public RejectDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ILogger<RejectDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task<Result> Handle(RejectDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Reject(request.Reason);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderRejected",
                $"Order '{order.OrderRef}' rejected by {request.RejectedBy ?? "system"}. Reason: {request.Reason}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Reject] Order {OrderId} '{OrderRef}' rejected. Reason: {Reason}.",
                order.Id, order.OrderRef, request.Reason);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Reject] Order {OrderId} reject failed: {Error}.", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Reject] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }
}
