using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarrierTypes;

public record CarrierTypeDto(
    Guid Id,
    string Code,
    string DisplayName,
    string AmrCapability,
    double? MaxWeightKg,
    int? MaxSlots,
    string? Description);

public record GetCarrierTypesQuery : IQuery<List<CarrierTypeDto>>;

public class GetCarrierTypesQueryHandler : IQueryHandler<GetCarrierTypesQuery, List<CarrierTypeDto>>
{
    private readonly ICarrierTypeRepository _repository;

    public GetCarrierTypesQueryHandler(ICarrierTypeRepository repository)
        => _repository = repository;

    public async Task<Result<List<CarrierTypeDto>>> Handle(
        GetCarrierTypesQuery request, CancellationToken cancellationToken)
    {
        var carrierTypes = await _repository.GetAllAsync(cancellationToken);
        return Result<List<CarrierTypeDto>>.Success(carrierTypes.Select(c => new CarrierTypeDto(
            c.Id, c.Code, c.DisplayName, c.AmrCapability,
            c.MaxWeightKg, c.MaxSlots, c.Description)).ToList());
    }
}
