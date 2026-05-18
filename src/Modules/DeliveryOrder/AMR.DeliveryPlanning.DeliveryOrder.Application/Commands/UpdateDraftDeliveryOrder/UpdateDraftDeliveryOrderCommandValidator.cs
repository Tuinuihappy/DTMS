using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public class UpdateDraftDeliveryOrderCommandValidator : AbstractValidator<UpdateDraftDeliveryOrderCommand>
{
    public UpdateDraftDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleFor(x => x.Items)
            .Must(items =>
            {
                var lotNos = items
                    .Where(i => i.CargoSpecific?.LotNo != null)
                    .Select(i => i.CargoSpecific!.LotNo!)
                    .ToList();
                return lotNos.Distinct(StringComparer.OrdinalIgnoreCase).Count() == lotNos.Count;
            })
            .WithMessage("LotNo must be unique within the order.");
        RuleForEach(x => x.Items).SetValidator(new ItemDtoValidator());
    }
}

file class ItemDtoValidator : AbstractValidator<ItemDto>
{
    public ItemDtoValidator()
    {
        RuleFor(p => p.Sku).NotEmpty().MaximumLength(100);
        RuleFor(p => p.CargoType).IsInEnum();
        RuleFor(p => p.PickupLocationCode).NotEmpty();
        RuleFor(p => p.DropLocationCode).NotEmpty();
        RuleFor(p => p.DropLocationCode)
            .NotEqual(p => p.PickupLocationCode)
            .WithMessage("Pickup and Drop locations must be different.");
        When(p => p.Dimensions != null, () =>
        {
            RuleFor(p => p.Dimensions!.LengthMm).GreaterThan(0);
            RuleFor(p => p.Dimensions!.WidthMm).GreaterThan(0);
            RuleFor(p => p.Dimensions!.HeightMm).GreaterThan(0);
        });
        When(p => p.WeightKg.HasValue, () =>
            RuleFor(p => p.WeightKg!.Value).GreaterThan(0));
        RuleFor(p => p.Quantity).NotNull();
        When(p => p.Quantity != null, () =>
        {
            RuleFor(p => p.Quantity.Value).GreaterThan(0);
            RuleFor(p => p.Quantity.Uom).NotEmpty().MaximumLength(20);
        });
    }
}
