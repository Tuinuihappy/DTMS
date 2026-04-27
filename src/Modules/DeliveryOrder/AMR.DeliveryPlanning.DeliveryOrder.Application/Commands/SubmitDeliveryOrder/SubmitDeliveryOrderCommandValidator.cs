using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandValidator : AbstractValidator<SubmitDeliveryOrderCommand>
{
    public SubmitDeliveryOrderCommandValidator(IOptions<DeliveryOrderOptions> options)
    {
        var minLeadTime = TimeSpan.FromMinutes(options.Value.MinimumSlaLeadTimeMinutes);

        RuleFor(x => x.OrderKey).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PickupLocationCode).NotEmpty();
        RuleFor(x => x.DropLocationCode).NotEmpty();
        RuleFor(x => x.DropLocationCode)
            .NotEqual(x => x.PickupLocationCode)
            .WithMessage("Pickup and Drop locations must be different.");

        RuleFor(x => x.SLA)
            .Must(sla => sla == null || sla > DateTime.UtcNow.Add(minLeadTime))
            .WithMessage($"SLA must be at least {minLeadTime.TotalMinutes} minutes in the future.");

        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one order line is required.");
        RuleForEach(x => x.Lines).ChildRules(lines =>
        {
            lines.RuleFor(l => l.ItemCode).NotEmpty();
            lines.RuleFor(l => l.Quantity).GreaterThan(0);
            lines.RuleFor(l => l.Weight).GreaterThan(0);
        });

        When(x => x.Schedule != null, () =>
        {
            RuleFor(x => x.Schedule!.CronExpression).NotEmpty();
        });
    }
}
