using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public class UpdateDraftDeliveryOrderCommandValidator : AbstractValidator<UpdateDraftDeliveryOrderCommand>
{
    public UpdateDraftDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleForEach(x => x.Items).SetValidator(new ItemDtoValidator());
    }
}

file class ItemDtoValidator : AbstractValidator<ItemDto>
{
    public ItemDtoValidator()
    {
        RuleFor(p => p.Sku).NotEmpty().MaximumLength(100);
        RuleFor(p => p.PickupLocationCode).NotEmpty();
        RuleFor(p => p.DropLocationCode).NotEmpty();
        RuleFor(p => p.DropLocationCode)
            .NotEqual(p => p.PickupLocationCode)
            .WithMessage("Pickup and Drop locations must be different.");
        When(p => p.Dimensions != null, () =>
        {
            RuleFor(p => p.Dimensions!.LengthCm).GreaterThan(0);
            RuleFor(p => p.Dimensions!.WidthCm).GreaterThan(0);
            RuleFor(p => p.Dimensions!.HeightCm).GreaterThan(0);
        });
        RuleFor(p => p.WeightKg).GreaterThan(0);
        RuleFor(p => p.Quantity).NotNull();
        When(p => p.Quantity != null, () =>
        {
            RuleFor(p => p.Quantity.Value).GreaterThan(0);
            RuleFor(p => p.Quantity.Uom).NotEmpty().MaximumLength(20);
        });
    }
}
