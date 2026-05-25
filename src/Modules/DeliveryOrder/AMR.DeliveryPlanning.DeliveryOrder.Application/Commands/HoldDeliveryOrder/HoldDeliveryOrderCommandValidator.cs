using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.HoldDeliveryOrder;

public class HoldDeliveryOrderCommandValidator : AbstractValidator<HoldDeliveryOrderCommand>
{
    public HoldDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.HeldBy!).MaximumLength(200).When(x => x.HeldBy != null);
    }
}
