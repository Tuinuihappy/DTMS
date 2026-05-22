using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

public class CreateUpstreamDeliveryOrderCommandHandler : ICommandHandler<CreateUpstreamDeliveryOrderCommand, UpstreamOrderAckDto>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IStationValidationService _stationValidation;
    private readonly IStationLookup _stationLookup;
    private readonly ILogger<CreateUpstreamDeliveryOrderCommandHandler> _logger;

    public CreateUpstreamDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IStationValidationService stationValidation,
        IStationLookup stationLookup,
        ILogger<CreateUpstreamDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _stationValidation = stationValidation;
        _stationLookup = stationLookup;
        _logger = logger;
    }

    public async Task<Result<UpstreamOrderAckDto>> Handle(CreateUpstreamDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        // Idempotency check — if (SourceSystem, OrderRef) already exists, return existing ack
        var existing = await _repository.GetByRefAsync(request.SourceSystem, request.OrderRef, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("[Upstream] Order '{OrderRef}' from {SourceSystem} already exists with id {OrderId} — returning existing.",
                request.OrderRef, request.SourceSystem, existing.Id);
            return Result<UpstreamOrderAckDto>.Success(
                new UpstreamOrderAckDto(existing.Id, existing.OrderRef, existing.Status, existing.CreatedDate));
        }

        Domain.Entities.DeliveryOrder order;
        try
        {
            order = Domain.Entities.DeliveryOrder.CreateFromUpstream(
                request.OrderRef, request.Priority, request.RequestedDeliveryDate,
                request.SourceSystem, request.CreatedBy);

            var normalize = await LocationCodeNormalizer.BuildAsync(
                request.Items.SelectMany(i => new[] { i.PickupLocationCode, i.DropLocationCode }),
                _stationLookup, cancellationToken);

            foreach (var (item, idx) in request.Items.Select((p, i) => (p, i + 1)))
            {
                order.AddItem(
                    normalize(item.PickupLocationCode), normalize(item.DropLocationCode),
                    idx, item.Sku, item.Description,
                    item.LoadUnitProfileCode,
                    item.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                    item.WeightKg,
                    item.Quantity.Value, item.Quantity.Uom,
                    item.CargoType,
                    item.CargoSpecific is { } cs
                        ? CargoSpecific.Create(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                        : null);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Upstream] Order '{OrderRef}' build failed: {Error}.", request.OrderRef, ex.Message);
            return Result<UpstreamOrderAckDto>.Failure(ex.Message);
        }

        var stationMap = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
        if (stationMap.IsFailure)
        {
            _logger.LogWarning("[Upstream] Order '{OrderRef}' station mapping failed: {Error}.", request.OrderRef, stationMap.Error);
            return Result<UpstreamOrderAckDto>.Failure(stationMap.Error);
        }

        try
        {
            order.MarkAsValidated(stationMap.Value);
            order.Confirm();

            await _repository.AddAsync(order, cancellationToken);
            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderUpstreamIngested",
                $"Order '{order.OrderRef}' ingested from {order.SourceSystem} by {request.CreatedBy} — auto-confirmed and queued for planning"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Upstream] Order {OrderId} '{OrderRef}' from {SourceSystem} auto-pipelined to ReadyToPlan.",
                order.Id, order.OrderRef, order.SourceSystem);

            return Result<UpstreamOrderAckDto>.Success(
                new UpstreamOrderAckDto(order.Id, order.OrderRef, order.Status, order.CreatedDate));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Upstream] Order '{OrderRef}' pipeline failed: {Error}.", request.OrderRef, ex.Message);
            return Result<UpstreamOrderAckDto>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Upstream] Concurrency conflict on Order '{OrderRef}'.", request.OrderRef);
            return Result<UpstreamOrderAckDto>.Failure("The order was modified by another process. Please retry.");
        }
        catch (DbUpdateException)
        {
            // Likely unique-index violation from a concurrent insert of the same (SourceSystem, OrderRef).
            // Re-query: if the row now exists, treat as idempotent success; otherwise surface the original error.
            var raced = await _repository.GetByRefAsync(request.SourceSystem, request.OrderRef, cancellationToken);
            if (raced is null) throw;

            _logger.LogInformation("[Upstream] Order '{OrderRef}' raced with concurrent insert — returning existing id {OrderId}.",
                request.OrderRef, raced.Id);
            return Result<UpstreamOrderAckDto>.Success(
                new UpstreamOrderAckDto(raced.Id, raced.OrderRef, raced.Status, raced.CreatedDate));
        }
    }
}
