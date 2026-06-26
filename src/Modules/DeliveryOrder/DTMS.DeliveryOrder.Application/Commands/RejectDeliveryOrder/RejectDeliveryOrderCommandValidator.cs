using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RejectDeliveryOrder;

public class RejectDeliveryOrderCommandValidator : AbstractValidator<RejectDeliveryOrderCommand>
{
    public RejectDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.RejectedBy!).MaximumLength(200).When(x => x.RejectedBy != null);
    }
}
