using FluentValidation;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.CreateMap;

public class CreateMapCommandValidator : AbstractValidator<CreateMapCommand>
{
    public CreateMapCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Version).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Width).GreaterThan(0);
        RuleFor(x => x.Height).GreaterThan(0);
        RuleFor(x => x.MapData).NotEmpty();
    }
}
