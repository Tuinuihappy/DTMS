using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.RegisterCarrierTypeProfile;

public record RegisterCarrierTypeProfileCommand(
    string Code,
    string DisplayName,
    string AMRCapability,
    double? MaxWeightKg = null,
    int? MaxSlots = null,
    string? Description = null) : ICommand<Guid>;

internal sealed class RegisterCarrierTypeProfileCommandHandler : ICommandHandler<RegisterCarrierTypeProfileCommand, Guid>
{
    private readonly ICarrierTypeProfileRepository _repository;

    public RegisterCarrierTypeProfileCommandHandler(ICarrierTypeProfileRepository repository)
        => _repository = repository;

    public async Task<Result<Guid>> Handle(RegisterCarrierTypeProfileCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
            return Result<Guid>.Failure($"CarrierTypeProfile '{request.Code.ToUpperInvariant()}' already exists.");

        var profile = new CarrierTypeProfile(
            request.Code, request.DisplayName, request.AMRCapability,
            request.MaxWeightKg, request.MaxSlots, request.Description);

        await _repository.AddAsync(profile, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(profile.Id);
    }
}
