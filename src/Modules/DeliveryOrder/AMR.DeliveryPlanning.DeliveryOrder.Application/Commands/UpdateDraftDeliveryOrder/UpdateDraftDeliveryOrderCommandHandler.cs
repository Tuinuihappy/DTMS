using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public class UpdateDraftDeliveryOrderCommandHandler : ICommandHandler<UpdateDraftDeliveryOrderCommand, DeliveryOrderDetailDto>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IUomNormalizer _uomNormalizer;
    private readonly ILogger<UpdateDraftDeliveryOrderCommandHandler> _logger;

    public UpdateDraftDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IUomNormalizer uomNormalizer,
        ILogger<UpdateDraftDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _uomNormalizer = uomNormalizer;
        _logger = logger;
    }

    public async Task<Result<DeliveryOrderDetailDto>> Handle(UpdateDraftDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<DeliveryOrderDetailDto>.Failure($"Order {request.OrderId} not found.");

        try
        {
            // Capture and remove old items BEFORE adding new ones so the
            // navigation fixer does not start tracking new items as Modified.
            var oldItems = order.Items.ToList();
            await _repository.RemoveItemsAsync(oldItems, cancellationToken);

            var serviceWindow = request.ServiceWindow is { } sw
                ? Domain.ValueObjects.ServiceWindow.Create(sw.Earliest, sw.Latest)
                : null;

            order.UpdateDraft(request.OrderRef, request.Priority, serviceWindow, request.SlaTier);

            foreach (var (item, idx) in request.Items.Select((p, i) => (p, i + 1)))
            {
                var uom = _uomNormalizer.Normalize(item.Quantity.Uom);
                if (uom is null)
                    return Result<DeliveryOrderDetailDto>.Failure(
                        $"Unknown UOM '{item.Quantity.Uom}' on item {idx} — accepted: KG, G, LB, EA, BOX, PALLET, CASE (or configured aliases).");

                order.AddItem(
                    item.PickupLocationCode, item.DropLocationCode,
                    idx, item.Sku, item.Description,
                    item.LoadUnitProfileCode,
                    item.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                    item.WeightKg,
                    Quantity.Create(item.Quantity.Value, uom.Value),
                    item.CargoType,
                    item.CargoSpecific is { } cs
                        ? CargoSpecific.Create(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                        : null,
                    item.Hazmat is { } hz
                        ? HazmatInfo.Create(hz.ClassCode, hz.PackingGroup)
                        : null);
            }

            // Explicitly mark new items as Added — EF Core's DetectChanges would otherwise
            // treat client-generated Guid keys as existing entities (Modified state).
            await _repository.AddItemsAsync(order.Items, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[UpdateDraft] Order {OrderId} '{OrderRef}' draft updated with {ItemCount} item(s).",
                order.Id, order.OrderRef, order.Items.Count);

            return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[UpdateDraft] Order {OrderId} update failed: {Error}.", request.OrderId, ex.Message);
            return Result<DeliveryOrderDetailDto>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[UpdateDraft] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<DeliveryOrderDetailDto>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
