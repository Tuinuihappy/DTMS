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

        RuleFor(x => x.SLA)
            .Must(sla => sla == null || sla > DateTime.UtcNow.Add(minLeadTime))
            .WithMessage($"SLA must be at least {minLeadTime.TotalMinutes} minutes in the future.");

        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one order line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PickupLocationCode).NotEmpty();
            line.RuleFor(l => l.DropLocationCode).NotEmpty();
            line.RuleFor(l => l.DropLocationCode)
                .NotEqual(l => l.PickupLocationCode)
                .WithMessage("Pickup and Drop locations must be different.");
            line.RuleFor(l => l.WorkOrderId).GreaterThan(0);
            line.RuleFor(l => l.WorkOrder).NotEmpty().MaximumLength(50);
            line.RuleFor(l => l.ItemId).GreaterThan(0);
            line.RuleFor(l => l.ItemNumber).NotEmpty().MaximumLength(50);
            line.RuleFor(l => l.ItemDescription).NotEmpty().MaximumLength(200);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.Weight).GreaterThan(0);
        });

        When(x => x.Schedule != null, () =>
        {
            RuleFor(x => x.Schedule!.CronExpression).NotEmpty();
        });
    }
}
