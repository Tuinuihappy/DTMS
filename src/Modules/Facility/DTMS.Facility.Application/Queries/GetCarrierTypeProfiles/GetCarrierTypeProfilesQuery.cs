using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Queries.GetCarrierTypeProfiles;

public record CarrierTypeProfileDto(
    Guid Id,
    string Code,
    string DisplayName,
    string AMRCapability,
    double? MaxWeightKg,
    int? MaxSlots,
    string? Description);

public record GetCarrierTypeProfilesQuery : IQuery<List<CarrierTypeProfileDto>>;

public class GetCarrierTypeProfilesQueryHandler : IQueryHandler<GetCarrierTypeProfilesQuery, List<CarrierTypeProfileDto>>
{
    private readonly ICarrierTypeProfileRepository _repository;

    public GetCarrierTypeProfilesQueryHandler(ICarrierTypeProfileRepository repository)
        => _repository = repository;

    public async Task<Result<List<CarrierTypeProfileDto>>> Handle(
        GetCarrierTypeProfilesQuery request, CancellationToken cancellationToken)
    {
        var profiles = await _repository.GetAllAsync(cancellationToken);
        return Result<List<CarrierTypeProfileDto>>.Success(profiles.Select(p => new CarrierTypeProfileDto(
            p.Id, p.Code, p.DisplayName, p.AMRCapability,
            p.MaxWeightKg, p.MaxSlots, p.Description)).ToList());
    }
}
