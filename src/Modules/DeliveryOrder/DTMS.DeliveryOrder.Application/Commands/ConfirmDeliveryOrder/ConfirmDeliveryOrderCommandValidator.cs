using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;

public class ConfirmDeliveryOrderCommandValidator : AbstractValidator<ConfirmDeliveryOrderCommand>
{
    public ConfirmDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ConfirmedBy!).MaximumLength(200).When(x => x.ConfirmedBy != null);
    }
}
