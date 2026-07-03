using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandHandler : ICommandHandler<SubmitDeliveryOrderCommand, SubmitDeliveryOrderResult>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IStationValidationService _stationValidation;
    private readonly DeliveryOrderOptions _options;
    private readonly ILogger<SubmitDeliveryOrderCommandHandler> _logger;

    public SubmitDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IStationValidationService stationValidation,
        IOptions<DeliveryOrderOptions> options,
        ILogger<SubmitDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _stationValidation = stationValidation;
        _options = options.Value;
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

        // WMS PR-2 — interpret location codes based on the order's
        // RequestedTransportMode. AMR keeps the existing station-code
        // semantics; Manual / Fleet resolve against the WMS snapshot
        // (wms.Locations) instead of internal Warehouse rows.
        var mode = order.RequestedTransportMode ?? TransportMode.Amr;
        IReadOnlyDictionary<string, Guid>? stationMap = null;
        IReadOnlyDictionary<string, Guid>? wmsLocationMap = null;

        if (mode == TransportMode.Amr)
        {
            var stationResult = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
            if (stationResult.IsFailure) return Result<SubmitDeliveryOrderResult>.Failure(stationResult.Error);
            stationMap = stationResult.Value;
        }
        else
        {
            // Manual / Fleet → WMS location-code interpretation.
            var wmsResult = await _stationValidation.BuildWmsLocationMapAsync(order.Items, cancellationToken);
            if (wmsResult.IsFailure) return Result<SubmitDeliveryOrderResult>.Failure(wmsResult.Error);
            wmsLocationMap = wmsResult.Value;
        }

        try
        {
            // Phase P5 — atomic Submit + Validate + Confirm, mirroring the
            // system path. Validated becomes a transient state that lasts
            // one method dispatch; no order is durably persisted at
            // Validated any more, so downstream consumers (Planning saga)
            // that already listen on DeliveryOrderConfirmedIntegrationEventV1
            // don't need to distinguish "system submit" from "user submit".
            order.Submit();
            order.MarkAsValidated(stationMap, wmsLocationMap);
            order.Confirm(_options.WeightFallbackKg);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderSubmitted",
                $"Order '{order.OrderRef}' submitted, validated, and confirmed with priority {order.Priority}"), cancellationToken);

            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            foreach (var w in warnings)
                await _auditRepo.AddAsync(new OrderAuditEvent(order.Id, "QualityWarning", $"{w.Code}: {w.Message}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Submit] Order {OrderId} '{OrderRef}' submitted → confirmed ({WarningCount} warning(s)).",
                order.Id, order.OrderRef, warnings.Count);

            return Result<SubmitDeliveryOrderResult>.Success(new SubmitDeliveryOrderResult(
                DeliveryOrderMapper.MapToDetailDto(order),
                warnings));
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
