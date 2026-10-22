using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using FluentValidation;

namespace DTMS.DeliveryOrder.Application.Commands.ReviseAndResendDeliveryOrder;

public class ReviseAndResendDeliveryOrderCommandValidator : AbstractValidator<ReviseAndResendDeliveryOrderCommand>
{
    public ReviseAndResendDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Priority).IsInEnum();

        RuleFor(x => x.ServiceWindow).NotNull();
        RuleFor(x => x.ServiceWindow)
            .Must(sw => sw.EarliestUtc.HasValue || sw.LatestUtc.HasValue)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow must have at least one bound (EarliestUtc or LatestUtc).");
        RuleFor(x => x.ServiceWindow)
            .Must(sw => !(sw.EarliestUtc.HasValue && sw.LatestUtc.HasValue) || sw.EarliestUtc.Value <= sw.LatestUtc.Value)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow.EarliestUtc must be on or before LatestUtc.");

        RuleFor(x => x.Items).NotEmpty().WithMessage("A revised order must keep at least one item.");
        // Same rules as a submit — the order goes straight back to dispatch.
        RuleForEach(x => x.Items).SetValidator(new SubmitItemDtoValidator());
    }
}
