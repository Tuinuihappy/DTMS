using System.Text.RegularExpressions;
using FluentValidation;

namespace DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class DraftItemDtoValidator : AbstractValidator<ItemDto>
{
    // Mirror of the domain regex on HazmatInfo.ClassCode — kept here too so
    // ModelValidation rejects bad input with a clear 400 before the handler
    // throws (which would still work, but as a generic Result.Failure).
    private static readonly Regex HazmatClassPattern =
        new(@"^[1-9](\.[1-6])?$", RegexOptions.Compiled);

    public DraftItemDtoValidator()
    {
        RuleFor(p => p.ItemId).MaximumLength(100);

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

        RuleFor(p => p.PickupLocationCode).NotEmpty().MaximumLength(50);
        RuleFor(p => p.DropLocationCode).NotEmpty().MaximumLength(50);

        RuleFor(p => p.DropLocationCode)
            .NotEqual(p => p.PickupLocationCode)
            .When(p => !string.IsNullOrWhiteSpace(p.PickupLocationCode) && !string.IsNullOrWhiteSpace(p.DropLocationCode))
            .WithMessage("Pickup and Drop locations must be different.");

        When(p => p.Hazmat is not null, () =>
        {
            RuleFor(p => p.Hazmat!.ClassCode)
                .NotEmpty()
                .Must(c => !string.IsNullOrWhiteSpace(c) && HazmatClassPattern.IsMatch(c.Trim()))
                .WithMessage("Hazmat.ClassCode must be a UN hazard class 1-9 with optional subdivision (e.g. '3', '2.1', '5.1').");

            RuleFor(p => p.Hazmat!.PackingGroup!.Value)
                .IsInEnum()
                .When(p => p.Hazmat!.PackingGroup.HasValue);
        });

        When(p => p.Temperature is not null, () =>
        {
            RuleFor(p => p.Temperature!)
                .Must(t => t.MinC.HasValue || t.MaxC.HasValue)
                .WithMessage("Temperature must have at least one bound (MinC or MaxC).");
            RuleFor(p => p.Temperature!)
                .Must(t => !(t.MinC.HasValue && t.MaxC.HasValue) || t.MinC!.Value <= t.MaxC!.Value)
                .WithMessage("Temperature.MinC must be on or below MaxC.");
        });

        When(p => p.HandlingInstructions is not null, () =>
        {
            RuleForEach(p => p.HandlingInstructions!)
                .IsInEnum()
                .WithMessage("HandlingInstructions must contain valid values: Fragile, ThisSideUp, DoNotStack, HeavyLift, Sharp, KeepDry, KeepDark, PinchHazard.");
        });
    }
}

public class SubmitItemDtoValidator : AbstractValidator<ItemDto>
{
    public SubmitItemDtoValidator()
    {
        Include(new DraftItemDtoValidator());

        RuleFor(p => p.ItemId).NotEmpty();
        RuleFor(p => p.Quantity).NotNull();
        RuleFor(p => p.Quantity.Uom).NotEmpty().When(p => p.Quantity != null);
    }
}
