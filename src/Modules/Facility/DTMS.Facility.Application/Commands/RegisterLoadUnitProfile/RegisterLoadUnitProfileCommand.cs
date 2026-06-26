using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.RegisterLoadUnitProfile;

public record RegisterLoadUnitProfileCommand(
    string Code,
    string DisplayName,
    double LengthMm,
    double WidthMm,
    double HeightMm,
    double MaxGrossWeightKg,
    string CarrierTypeCode) : ICommand<Guid>;

internal sealed class RegisterLoadUnitProfileCommandHandler : ICommandHandler<RegisterLoadUnitProfileCommand, Guid>
{
    private readonly ILoadUnitProfileRepository _repository;
    private readonly ICarrierTypeProfileRepository _carrierTypeRepository;

    public RegisterLoadUnitProfileCommandHandler(
        ILoadUnitProfileRepository repository,
        ICarrierTypeProfileRepository carrierTypeRepository)
    {
        _repository = repository;
        _carrierTypeRepository = carrierTypeRepository;
    }

    public async Task<Result<Guid>> Handle(RegisterLoadUnitProfileCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
            return Result<Guid>.Failure($"LoadUnitProfile '{request.Code.ToUpperInvariant()}' already exists.");

        var carrierType = await _carrierTypeRepository.GetByCodeAsync(request.CarrierTypeCode, cancellationToken);
        if (carrierType is null)
            return Result<Guid>.Failure($"CarrierTypeCode '{request.CarrierTypeCode}' not found.");

        var profile = new LoadUnitProfile(
            request.Code, request.DisplayName,
            request.LengthMm, request.WidthMm, request.HeightMm,
            request.MaxGrossWeightKg, request.CarrierTypeCode);

        await _repository.AddAsync(profile, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(profile.Id);
    }
}
