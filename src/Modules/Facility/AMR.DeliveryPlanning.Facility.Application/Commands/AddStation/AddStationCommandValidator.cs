using FluentValidation;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.AddStation;

public class AddStationCommandValidator : AbstractValidator<AddStationCommand>
{
    public AddStationCommandValidator()
    {
        RuleFor(x => x.MapId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.X).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Code)
            .MaximumLength(50)
            .Matches(@"^[A-Z0-9][A-Z0-9\-_]*$")
            .WithMessage("Code must be uppercase alphanumeric with hyphens or underscores (e.g. WH-NORTH).")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));
    }
}
