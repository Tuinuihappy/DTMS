using FluentValidation;
using FluentValidation.Results;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

/// <summary>
/// Strict checks applied to a CreateDraft-shaped command when the caller is about
/// to submit. Used by <c>BulkSubmitDeliveryOrdersCommandHandler</c> which receives
/// a list of CreateDraft commands intended for immediate submission.
///
/// Intentionally NOT an AbstractValidator&lt;TCommand&gt; — that would be auto-registered
/// and run on every CreateDraft call, defeating the loose-draft contract.
/// Handlers must invoke this explicitly.
/// </summary>
public static class SubmitReadiness
{
    private static readonly InlineValidator<CreateDraftDeliveryOrderCommand> Validator = Build();

    private static InlineValidator<CreateDraftDeliveryOrderCommand> Build()
    {
        var v = new InlineValidator<CreateDraftDeliveryOrderCommand>();
        v.Include(new CreateDraftDeliveryOrderCommandValidator());
        v.RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        v.RuleForEach(x => x.Items).SetValidator(new SubmitItemDtoValidator());
        return v;
    }

    public static ValidationResult Validate(CreateDraftDeliveryOrderCommand cmd) => Validator.Validate(cmd);
}
