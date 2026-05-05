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

        RuleFor(x => x.OrderItems).NotEmpty().WithMessage("At least one order item is required.");
        RuleForEach(x => x.OrderItems).ChildRules(item =>
        {
            item.RuleFor(i => i.PickupLocationCode).NotEmpty();
            item.RuleFor(i => i.DropLocationCode).NotEmpty();
            item.RuleFor(i => i.DropLocationCode)
                .NotEqual(i => i.PickupLocationCode)
                .WithMessage("Pickup and Drop locations must be different.");
            item.RuleFor(i => i.WorkOrder).MaximumLength(50).When(i => i.WorkOrder != null);
            item.RuleFor(i => i.ItemNumber).NotEmpty().MaximumLength(50);
            item.RuleFor(i => i.ItemDescription).NotEmpty().MaximumLength(200);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.Weight).GreaterThan(0).When(i => i.Weight.HasValue);
            item.RuleFor(i => i.HazmatClass)
                .InclusiveBetween(1, 9)
                .When(i => i.HazmatClass.HasValue)
                .WithMessage("HazmatClass must be between 1 and 9 (UN classification).");
            item.RuleFor(i => i.Line).MaximumLength(100).When(i => i.Line != null);
            item.RuleFor(i => i.Model).MaximumLength(100).When(i => i.Model != null);
        });

        When(x => x.Schedule != null, () =>
        {
            RuleFor(x => x.Schedule!.CronExpression).NotEmpty();
        });
    }
}
