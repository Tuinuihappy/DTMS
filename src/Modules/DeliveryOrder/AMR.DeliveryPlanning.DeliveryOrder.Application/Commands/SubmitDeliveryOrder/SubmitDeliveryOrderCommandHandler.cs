using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandHandler : ICommandHandler<SubmitDeliveryOrderCommand, SubmitDeliveryOrderResult>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IStationValidationService _stationValidation;
    private readonly ILogger<SubmitDeliveryOrderCommandHandler> _logger;

    public SubmitDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IStationValidationService stationValidation,
        ILogger<SubmitDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _stationValidation = stationValidation;
        _logger = logger;
    }

    public async Task<Result<SubmitDeliveryOrderResult>> Handle(SubmitDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<SubmitDeliveryOrderResult>.Failure($"Order {request.OrderId} not found.");

        var readiness = SubmitReadinessCheck.Check(order);
        if (!readiness.IsValid)
        {
            _logger.LogWarning("[Submit] Order {OrderId} not ready to submit: {Error}.", request.OrderId, readiness.Error);
            return Result<SubmitDeliveryOrderResult>.Failure(readiness.Error);
        }

        var stationMap = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
        if (stationMap.IsFailure) return Result<SubmitDeliveryOrderResult>.Failure(stationMap.Error);

        try
        {
            order.Submit();
            order.MarkAsValidated(stationMap.Value);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderSubmitted",
                $"Order '{order.OrderRef}' submitted and validated with priority {order.Priority}"), cancellationToken);

            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            foreach (var w in warnings)
                await _auditRepo.AddAsync(new OrderAuditEvent(order.Id, "QualityWarning", $"{w.Code}: {w.Message}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Submit] Order {OrderId} '{OrderRef}' submitted and validated ({WarningCount} warning(s)) — awaiting confirmation.",
                order.Id, order.OrderRef, warnings.Count);

            return Result<SubmitDeliveryOrderResult>.Success(new SubmitDeliveryOrderResult(order.Id, warnings));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Submit] Order {OrderId} submit failed: {Error}.", request.OrderId, ex.Message);
            return Result<SubmitDeliveryOrderResult>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Submit] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<SubmitDeliveryOrderResult>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
