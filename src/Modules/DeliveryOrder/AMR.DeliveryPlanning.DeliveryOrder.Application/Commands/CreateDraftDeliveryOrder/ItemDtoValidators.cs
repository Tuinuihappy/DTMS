using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class DraftItemDtoValidator : AbstractValidator<ItemDto>
{
    public DraftItemDtoValidator()
    {
        RuleFor(p => p.Sku).MaximumLength(100);

        RuleFor(p => p.CargoType!.Value)
            .IsInEnum()
            .When(p => p.CargoType != null);

        RuleFor(p => p.CargoSpecific)
            .Null()
            .When(p => p.CargoType == null)
            .WithMessage("CargoSpecific must not be provided when CargoType is empty.");

        RuleFor(p => p.LoadUnitProfileCode!)
            .MaximumLength(50)
            .When(p => p.LoadUnitProfileCode != null);

        When(p => p.Dimensions != null, () =>
        {
            RuleFor(p => p.Dimensions!.LengthMm).GreaterThan(0);
            RuleFor(p => p.Dimensions!.WidthMm).GreaterThan(0);
            RuleFor(p => p.Dimensions!.HeightMm).GreaterThan(0);
        });

        When(p => p.WeightKg.HasValue, () =>
            RuleFor(p => p.WeightKg!.Value).GreaterThan(0));

        When(p => p.Quantity != null, () =>
        {
            RuleFor(p => p.Quantity.Value).GreaterThan(0);
            RuleFor(p => p.Quantity.Uom).MaximumLength(20);
        });

        RuleFor(p => p.DropLocationCode)
            .NotEqual(p => p.PickupLocationCode)
            .When(p => !string.IsNullOrEmpty(p.PickupLocationCode) && !string.IsNullOrEmpty(p.DropLocationCode))
            .WithMessage("Pickup and Drop locations must be different.");
    }
}

public class SubmitItemDtoValidator : AbstractValidator<ItemDto>
{
    public SubmitItemDtoValidator()
    {
        Include(new DraftItemDtoValidator());

        RuleFor(p => p.Sku).NotEmpty();
        RuleFor(p => p.PickupLocationCode).NotEmpty();
        RuleFor(p => p.DropLocationCode).NotEmpty();
        RuleFor(p => p.Quantity).NotNull();
        RuleFor(p => p.Quantity.Uom).NotEmpty().When(p => p.Quantity != null);
    }
}
