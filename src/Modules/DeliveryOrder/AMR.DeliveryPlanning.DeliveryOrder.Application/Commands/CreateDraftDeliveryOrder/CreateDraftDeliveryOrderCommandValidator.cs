using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandValidator : AbstractValidator<CreateDraftDeliveryOrderCommand>
{
    public CreateDraftDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one package is required.");
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
        RuleForEach(x => x.Items).ChildRules(pkg =>
        {
            pkg.RuleFor(p => p.CargoType).IsInEnum();
            pkg.RuleFor(p => p.PickupLocationCode).NotEmpty();
            pkg.RuleFor(p => p.DropLocationCode).NotEmpty();
            pkg.RuleFor(p => p.DropLocationCode)
                .NotEqual(p => p.PickupLocationCode)
                .WithMessage("Pickup and Drop locations must be different.");
            pkg.RuleFor(p => p.Sku).NotEmpty().MaximumLength(100);
            pkg.RuleFor(p => p.LoadUnitProfileCode!)
                .NotEmpty()
                .MaximumLength(50)
                .When(p => p.LoadUnitProfileCode != null);
            pkg.When(p => p.Dimensions != null, () =>
            {
                pkg.RuleFor(p => p.Dimensions!.LengthMm).GreaterThan(0);
                pkg.RuleFor(p => p.Dimensions!.WidthMm).GreaterThan(0);
                pkg.RuleFor(p => p.Dimensions!.HeightMm).GreaterThan(0);
            });
            pkg.When(p => p.WeightKg.HasValue, () =>
                pkg.RuleFor(p => p.WeightKg!.Value).GreaterThan(0));
            pkg.RuleFor(p => p.Quantity).NotNull();
            pkg.When(p => p.Quantity != null, () =>
            {
                pkg.RuleFor(p => p.Quantity.Value).GreaterThan(0);
                pkg.RuleFor(p => p.Quantity.Uom).NotEmpty().MaximumLength(20);
            });
        });
    }
}
