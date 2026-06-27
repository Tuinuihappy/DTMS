using FluentValidation;

namespace DTMS.DeliveryOrder.Application.Commands.ReleaseDeliveryOrder;

public class ReleaseDeliveryOrderCommandValidator : AbstractValidator<ReleaseDeliveryOrderCommand>
{
    public ReleaseDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ReleasedBy!).MaximumLength(200).When(x => x.ReleasedBy != null);
    }
}
