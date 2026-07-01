using FluentValidation;

namespace DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandValidator : AbstractValidator<CreateDraftDeliveryOrderCommand>
{
    public CreateDraftDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes!).MaximumLength(1000).When(x => x.Notes != null);
        // Phase P5 — ServiceWindow is required, mirroring the system path.
        // The nested Must() rules run only when the outer NotNull passes,
        // so a missing ServiceWindow surfaces the required-error rather
        // than a follow-on NRE-shaped complaint about the bound values.
        RuleFor(x => x.ServiceWindow)
            .NotNull()
            .WithMessage("ServiceWindow is required.");
        RuleFor(x => x.ServiceWindow)
            .Must(sw => sw.EarliestUtc.HasValue || sw.LatestUtc.HasValue)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow must have at least one bound (EarliestUtc or LatestUtc).");
        RuleFor(x => x.ServiceWindow)
            .Must(sw => !(sw.EarliestUtc.HasValue && sw.LatestUtc.HasValue) || sw.EarliestUtc.Value <= sw.LatestUtc.Value)
            .When(x => x.ServiceWindow is not null)
            .WithMessage("ServiceWindow.EarliestUtc must be on or before LatestUtc.");

        RuleForEach(x => x.Items).SetValidator(new DraftItemDtoValidator());
    }
}
