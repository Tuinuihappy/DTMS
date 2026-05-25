using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandHandler : ICommandHandler<CreateDraftDeliveryOrderCommand, DeliveryOrderDetailDto>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<CreateDraftDeliveryOrderCommandHandler> _logger;

    public CreateDraftDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<CreateDraftDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<DeliveryOrderDetailDto>> Handle(CreateDraftDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var serviceWindow = request.ServiceWindow is { } sw
            ? Domain.ValueObjects.ServiceWindow.Create(sw.Earliest, sw.Latest)
            : null;

        var order = Domain.Entities.DeliveryOrder.Create(
            request.OrderRef,
            request.Priority,
            serviceWindow,
            request.SourceSystem,
            request.CreatedBy,
            request.SlaTier);

        foreach (var (pkg, idx) in request.Items.Select((p, i) => (p, i + 1)))
        {
            order.AddItem(
                pkg.PickupLocationCode, pkg.DropLocationCode,
                idx, pkg.Sku, pkg.Description,
                pkg.LoadUnitProfileCode,
                pkg.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                pkg.WeightKg,
                pkg.Quantity.Value,
                pkg.Quantity.Uom,
                pkg.CargoType,
                pkg.CargoSpecific is { } cs
                    ? CargoSpecific.Create(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                    : null);
        }

        await _repository.AddAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[CreateDraft] Order {OrderId} '{OrderRef}' created as Draft.", order.Id, order.OrderRef);

        return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
    }
}
