using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReopenDeliveryOrder;

public class ReopenDeliveryOrderCommandHandler : ICommandHandler<ReopenDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ILogger<ReopenDeliveryOrderCommandHandler> _logger;

    public ReopenDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ILogger<ReopenDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task<Result> Handle(ReopenDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReopenedBy))
            return Result.Failure("ReopenedBy is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("Reason is required.");

        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Reopen(request.Reason);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderReopened",
                $"Order '{order.OrderRef}' reopened by {request.ReopenedBy}: {request.Reason}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Reopen] Order {OrderId} '{OrderRef}' reopened by {By}: {Reason}",
                order.Id, order.OrderRef, request.ReopenedBy, request.Reason);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Reopen] Order {OrderId} reopen rejected: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Reopen] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }
}
