using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandValidator : AbstractValidator<SubmitDeliveryOrderCommand>
{
    public SubmitDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
