using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Commands.ReleaseDeliveryOrder;

public class ReleaseDeliveryOrderCommandHandler : ICommandHandler<ReleaseDeliveryOrderCommand, ReleaseDeliveryOrderResult>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly DeliveryOrderOptions _options;
    private readonly ILogger<ReleaseDeliveryOrderCommandHandler> _logger;

    public ReleaseDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IOptions<DeliveryOrderOptions> options,
        ILogger<ReleaseDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ReleaseDeliveryOrderResult>> Handle(ReleaseDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ReleaseDeliveryOrderResult>.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Release(_options.WeightFallbackKg);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderReleased",
                $"Order '{order.OrderRef}' released by {request.ReleasedBy ?? "system"} — re-queued for planning"), cancellationToken);

            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            foreach (var w in warnings)
                await _auditRepo.AddAsync(new OrderAuditEvent(order.Id, "QualityWarning", $"{w.Code}: {w.Message}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Release] Order {OrderId} '{OrderRef}' released back to Confirmed ({WarningCount} warning(s)).",
                order.Id, order.OrderRef, warnings.Count);

            return Result<ReleaseDeliveryOrderResult>.Success(new ReleaseDeliveryOrderResult(order.Id, warnings));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Release] Order {OrderId} release failed: {Error}.", request.OrderId, ex.Message);
            return Result<ReleaseDeliveryOrderResult>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Release] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<ReleaseDeliveryOrderResult>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
