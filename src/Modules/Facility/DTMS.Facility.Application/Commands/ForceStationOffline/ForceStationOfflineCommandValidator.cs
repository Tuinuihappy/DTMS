using FluentValidation;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.ForceStationOffline;

public class ForceStationOfflineCommandValidator : AbstractValidator<ForceStationOfflineCommand>
{
    // Bounds: at least 5 minutes (avoid trivial overrides), at most 24 hours
    // (longer than a day should be a proper deactivation, not an override).
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 24 * 60;

    public ForceStationOfflineCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(MinDurationMinutes, MaxDurationMinutes)
            .WithMessage($"DurationMinutes must be between {MinDurationMinutes} and {MaxDurationMinutes}.");
        RuleFor(x => x.By!).MaximumLength(200).When(x => x.By != null);
    }
}
