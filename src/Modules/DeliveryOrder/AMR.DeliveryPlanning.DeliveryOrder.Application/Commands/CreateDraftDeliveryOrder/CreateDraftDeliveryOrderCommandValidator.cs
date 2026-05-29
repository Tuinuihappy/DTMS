using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandValidator : AbstractValidator<CreateDraftDeliveryOrderCommand>
{
    public CreateDraftDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RequestedBy!).MaximumLength(200).When(x => x.RequestedBy != null);
        RuleFor(x => x.Notes!).MaximumLength(1000).When(x => x.Notes != null);
        // Mirror the domain invariant so callers get a clean 400 instead of a
        // 500 from ServiceWindow.Create throwing ArgumentException downstream.
        RuleFor(x => x.ServiceWindow!)
            .Must(sw => sw.EarliestUtc.HasValue || sw.LatestUtc.HasValue)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow must have at least one bound (EarliestUtc or LatestUtc).");
        RuleFor(x => x.ServiceWindow!)
            .Must(sw => !(sw.EarliestUtc.HasValue && sw.LatestUtc.HasValue) || sw.EarliestUtc.Value <= sw.LatestUtc.Value)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow.EarliestUtc must be on or before LatestUtc.");

        RuleForEach(x => x.Items).SetValidator(new DraftItemDtoValidator());
    }
}
