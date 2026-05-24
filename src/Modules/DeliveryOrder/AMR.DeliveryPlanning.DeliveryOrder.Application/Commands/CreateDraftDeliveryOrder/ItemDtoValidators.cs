using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class LocationRefDtoValidator : AbstractValidator<LocationRefDto>
{
    public LocationRefDtoValidator()
    {
        RuleFor(x => x)
            .Must(r => (r.Code is not null) ^ r.StationId.HasValue)
            .WithMessage("LocationRef must specify exactly one of Code or StationId.");

        RuleFor(x => x.Code!)
            .NotEmpty().MaximumLength(50)
            .When(x => x.Code is not null);

        RuleFor(x => x.StationId!.Value)
            .NotEqual(Guid.Empty)
            .When(x => x.StationId.HasValue);
    }
}

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

        RuleFor(p => p.PickupLocation).NotNull().SetValidator(new LocationRefDtoValidator());
        RuleFor(p => p.DropLocation).NotNull().SetValidator(new LocationRefDtoValidator());

        RuleFor(p => p.DropLocation)
            .NotEqual(p => p.PickupLocation)
            .When(p => p.PickupLocation is not null && p.DropLocation is not null)
            .WithMessage("Pickup and Drop locations must be different.");
    }
}

public class SubmitItemDtoValidator : AbstractValidator<ItemDto>
{
    public SubmitItemDtoValidator()
    {
        Include(new DraftItemDtoValidator());

        RuleFor(p => p.Sku).NotEmpty();
        RuleFor(p => p.Quantity).NotNull();
        RuleFor(p => p.Quantity.Uom).NotEmpty().When(p => p.Quantity != null);
    }
}
