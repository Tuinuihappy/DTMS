using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Queries.GetLoadUnitProfiles;

public record LoadUnitProfileDto(
    Guid Id,
    string Code,
    string DisplayName,
    double LengthMm,
    double WidthMm,
    double HeightMm,
    double MaxGrossWeightKg,
    string CarrierTypeCode);

public record GetLoadUnitProfilesQuery(string? CarrierTypeCode = null) : IQuery<List<LoadUnitProfileDto>>;

public class GetLoadUnitProfilesQueryHandler : IQueryHandler<GetLoadUnitProfilesQuery, List<LoadUnitProfileDto>>
{
    private readonly ILoadUnitProfileRepository _repository;

    public GetLoadUnitProfilesQueryHandler(ILoadUnitProfileRepository repository)
        => _repository = repository;

    public async Task<Result<List<LoadUnitProfileDto>>> Handle(
        GetLoadUnitProfilesQuery request, CancellationToken cancellationToken)
    {
        var profiles = request.CarrierTypeCode is not null
            ? await _repository.GetByCarrierTypeAsync(request.CarrierTypeCode, cancellationToken)
            : await _repository.GetAllAsync(cancellationToken);

        return Result<List<LoadUnitProfileDto>>.Success(profiles.Select(p => new LoadUnitProfileDto(
            p.Id, p.Code, p.DisplayName,
            p.LengthMm, p.WidthMm, p.HeightMm,
            p.MaxGrossWeightKg, p.CarrierTypeCode)).ToList());
    }
}
