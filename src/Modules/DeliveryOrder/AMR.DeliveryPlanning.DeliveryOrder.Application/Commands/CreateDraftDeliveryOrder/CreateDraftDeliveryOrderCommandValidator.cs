using FluentValidation;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandValidator : AbstractValidator<CreateDraftDeliveryOrderCommand>
{
    public CreateDraftDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrderRef).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Items)
            .Must(items =>
            {
                if (items == null) return true;
                var lotNos = items
                    .Where(i => i.CargoSpecific?.LotNo != null)
                    .Select(i => i.CargoSpecific!.LotNo!)
                    .ToList();
                return lotNos.Distinct(StringComparer.OrdinalIgnoreCase).Count() == lotNos.Count;
            })
            .WithMessage("LotNo must be unique within the order.");

        RuleForEach(x => x.Items).SetValidator(new DraftItemDtoValidator());
    }
}
