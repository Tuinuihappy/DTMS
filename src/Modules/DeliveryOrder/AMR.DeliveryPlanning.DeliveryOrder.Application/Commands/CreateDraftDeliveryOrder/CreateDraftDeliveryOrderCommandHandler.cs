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
        var order = Domain.Entities.DeliveryOrder.Create(
            request.OrderRef,
            request.Priority,
            request.RequestedDeliveryDate,
            request.SourceSystem);

        foreach (var pkg in request.Items)
        {
            order.AddItem(
                pkg.PickupLocationCode, pkg.DropLocationCode,
                pkg.ItemSeq, pkg.Sku,
                pkg.CargoType,
                pkg.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                pkg.WeightKg,
                pkg.Quantity.Value,
                pkg.Quantity.Uom,
                pkg.CargoSpecific is { } cs
                    ? CargoSpecific.Create(cs.PartNo, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                    : null);
        }

        await _repository.AddAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[CreateDraft] Order {OrderId} '{OrderRef}' created as Draft.", order.Id, order.OrderRef);

        return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
    }
}
