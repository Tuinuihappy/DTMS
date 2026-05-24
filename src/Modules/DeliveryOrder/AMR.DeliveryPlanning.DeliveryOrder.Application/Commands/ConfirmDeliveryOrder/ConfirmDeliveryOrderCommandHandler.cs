using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;

public class ConfirmDeliveryOrderCommandHandler : ICommandHandler<ConfirmDeliveryOrderCommand, ConfirmDeliveryOrderResult>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly DeliveryOrderOptions _options;
    private readonly ILogger<ConfirmDeliveryOrderCommandHandler> _logger;

    public ConfirmDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IOptions<DeliveryOrderOptions> options,
        ILogger<ConfirmDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ConfirmDeliveryOrderResult>> Handle(ConfirmDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ConfirmDeliveryOrderResult>.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Confirm(_options.WeightFallbackKg);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderConfirmed",
                $"Order '{order.OrderRef}' confirmed by {request.ConfirmedBy ?? "system"} and queued for planning"), cancellationToken);

            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            foreach (var w in warnings)
                await _auditRepo.AddAsync(new OrderAuditEvent(order.Id, "QualityWarning", $"{w.Code}: {w.Message}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Confirm] Order {OrderId} '{OrderRef}' confirmed ({WarningCount} warning(s)) — planning will pick up.",
                order.Id, order.OrderRef, warnings.Count);

            return Result<ConfirmDeliveryOrderResult>.Success(new ConfirmDeliveryOrderResult(order.Id, warnings));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Confirm] Order {OrderId} confirm failed: {Error}.", request.OrderId, ex.Message);
            return Result<ConfirmDeliveryOrderResult>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Confirm] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<ConfirmDeliveryOrderResult>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
