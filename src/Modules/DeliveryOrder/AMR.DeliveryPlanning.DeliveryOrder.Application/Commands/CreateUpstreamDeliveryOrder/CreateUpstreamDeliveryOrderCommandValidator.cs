using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

public class CreateUpstreamDeliveryOrderCommandValidator : AbstractValidator<CreateUpstreamDeliveryOrderCommand>
{
    public CreateUpstreamDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SourceSystem)
            .NotEqual(SourceSystem.Manual)
            .WithMessage("Upstream orders cannot have Manual source system.");
        RuleFor(x => x.RequestedDeliveryDate).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(p => p.PickupLocationCode).NotEmpty();
            item.RuleFor(p => p.DropLocationCode).NotEmpty();
            item.RuleFor(p => p.DropLocationCode)
                .NotEqual(p => p.PickupLocationCode)
                .WithMessage("Pickup and Drop locations must be different.");
            item.RuleFor(p => p.Sku).NotEmpty().MaximumLength(100);
            item.RuleFor(p => p.WeightKg)
                .NotNull().WithMessage("WeightKg is required for upstream orders.")
                .GreaterThan(0).When(p => p.WeightKg.HasValue);
            item.RuleFor(p => p.Quantity).NotNull();
            item.When(p => p.Quantity != null, () =>
            {
                item.RuleFor(p => p.Quantity.Value).GreaterThan(0);
                item.RuleFor(p => p.Quantity.Uom).NotEmpty().MaximumLength(20);
            });
            item.When(p => p.Dimensions != null, () =>
            {
                item.RuleFor(p => p.Dimensions!.LengthMm).GreaterThan(0);
                item.RuleFor(p => p.Dimensions!.WidthMm).GreaterThan(0);
                item.RuleFor(p => p.Dimensions!.HeightMm).GreaterThan(0);
            });
            item.RuleFor(p => p.CargoSpecific)
                .Null()
                .When(p => p.CargoType == null)
                .WithMessage("CargoSpecific must not be provided when CargoType is empty.");
            item.RuleFor(p => p.LoadUnitProfileCode!)
                .NotEmpty().MaximumLength(50)
                .When(p => p.LoadUnitProfileCode != null);
        });
    }
}
