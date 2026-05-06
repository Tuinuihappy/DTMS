using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandValidator : AbstractValidator<CreateDraftDeliveryOrderCommand>
{
    public CreateDraftDeliveryOrderCommandValidator(IOptions<DeliveryOrderOptions> options)
    {
        var minLeadTime = TimeSpan.FromMinutes(options.Value.MinimumSlaLeadTimeMinutes);

        RuleFor(x => x.OrderName).NotEmpty().MaximumLength(200);

        When(x => x.ServiceWindow?.Latest != null, () =>
        {
            RuleFor(x => x.ServiceWindow!.Latest)
                .Must(latest => latest > DateTime.UtcNow.Add(minLeadTime))
                .WithMessage($"ServiceWindow.Latest must be at least {minLeadTime.TotalMinutes} minutes in the future.");
        });

        When(x => x.ServiceWindow?.Earliest != null && x.ServiceWindow?.Latest != null, () =>
        {
            RuleFor(x => x.ServiceWindow)
                .Must(sw => sw!.Earliest < sw.Latest)
                .WithMessage("ServiceWindow.Earliest must be before ServiceWindow.Latest.");
        });

        RuleFor(x => x.OrderItems).NotEmpty().WithMessage("At least one package is required.");
        RuleForEach(x => x.OrderItems).ChildRules(pkg =>
        {
            pkg.RuleFor(p => p.PickupLocationCode).NotEmpty();
            pkg.RuleFor(p => p.DropLocationCode).NotEmpty();
            pkg.RuleFor(p => p.DropLocationCode)
                .NotEqual(p => p.PickupLocationCode)
                .WithMessage("Pickup and Drop locations must be different.");
            pkg.RuleFor(p => p.Barcode).NotEmpty().MaximumLength(100);
            pkg.RuleFor(p => p.LoadUnitProfileCode).NotEmpty().MaximumLength(50);
            pkg.RuleFor(p => p.GrossWeightKg).GreaterThan(0);
            pkg.RuleForEach(p => p.Contents).ChildRules(content =>
            {
                content.RuleFor(c => c.ItemNumber).NotEmpty().MaximumLength(100);
                content.RuleFor(c => c.Quantity).GreaterThan(0);
            }).When(p => p.Contents is { Count: > 0 });
        });

        When(x => x.Schedule != null, () =>
        {
            RuleFor(x => x.Schedule!.CronExpression).NotEmpty();
        });
    }
}
