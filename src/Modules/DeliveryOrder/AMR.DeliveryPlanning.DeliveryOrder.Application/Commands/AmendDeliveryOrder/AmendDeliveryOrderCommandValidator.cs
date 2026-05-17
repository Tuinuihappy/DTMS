using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public class AmendDeliveryOrderCommandValidator : AbstractValidator<AmendDeliveryOrderCommand>
{
    public AmendDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.NewRequestedDeliveryDate).NotNull()
            .WithMessage("At least one amendment field (NewRequestedDeliveryDate) must be provided.");
        RuleFor(x => x.AmendedBy).MaximumLength(200).When(x => x.AmendedBy != null);
    }
}
