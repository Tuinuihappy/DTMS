using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;

public class ConfirmDeliveryOrderCommandHandler : ICommandHandler<ConfirmDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ILogger<ConfirmDeliveryOrderCommandHandler> _logger;

    public ConfirmDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ILogger<ConfirmDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(ConfirmDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<Guid>.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Confirm();

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderConfirmed",
                $"Order '{order.OrderRef}' confirmed by {request.ConfirmedBy ?? "system"} and queued for planning"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Confirm] Order {OrderId} '{OrderRef}' confirmed — planning will pick up.",
                order.Id, order.OrderRef);

            return Result<Guid>.Success(order.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Confirm] Order {OrderId} confirm failed: {Error}.", request.OrderId, ex.Message);
            return Result<Guid>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Confirm] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<Guid>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
