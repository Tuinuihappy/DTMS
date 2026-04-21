using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandValidator : AbstractValidator<SubmitDeliveryOrderCommand>
{
    public SubmitDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderKey).NotEmpty();
        RuleFor(x => x.PickupLocationCode).NotEmpty();
        RuleFor(x => x.DropLocationCode).NotEmpty();
        RuleFor(x => x.DropLocationCode).NotEqual(x => x.PickupLocationCode).WithMessage("Pickup and Drop locations must be different.");
        
        RuleFor(x => x.SLA).GreaterThan(DateTime.UtcNow).When(x => x.SLA.HasValue).WithMessage("SLA must be in the future.");

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
