using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
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
        RuleFor(x => x.ServiceWindow).NotNull()
            .WithMessage("Upstream orders must include a ServiceWindow.");
        RuleFor(x => x.ServiceWindow)
            .Must(sw => sw.Earliest.HasValue || sw.Latest.HasValue)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow must have at least one bound (Earliest or Latest).");
        RuleFor(x => x.CreatedBy).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(p => p.PickupLocationCode).NotEmpty().MaximumLength(50);
            item.RuleFor(p => p.DropLocationCode).NotEmpty().MaximumLength(50);
            item.RuleFor(p => p.DropLocationCode)
                .NotEqual(p => p.PickupLocationCode)
                .When(p => !string.IsNullOrWhiteSpace(p.PickupLocationCode) && !string.IsNullOrWhiteSpace(p.DropLocationCode))
                .WithMessage("Pickup and Drop locations must be different.");
            item.RuleFor(p => p.Sku).NotEmpty().MaximumLength(100);
            // WeightKg is optional everywhere (P0-5 / Option C). When omitted, the order
            // is still accepted, a warning is returned to the caller, and the configured
            // WeightFallbackKg is used in the planning event so capacity stays safe.
            item.RuleFor(p => p.WeightKg!.Value)
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
