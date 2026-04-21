using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Queries.GetAvailableVehicles;

internal sealed class GetAvailableVehiclesQueryHandler : IQueryHandler<GetAvailableVehiclesQuery, IReadOnlyList<VehicleDto>>
{
    private readonly IVehicleRepository _vehicleRepository;

    public GetAvailableVehiclesQueryHandler(IVehicleRepository vehicleRepository)
    {
        _vehicleRepository = vehicleRepository;
    }

    public async Task<Result<IReadOnlyList<VehicleDto>>> Handle(GetAvailableVehiclesQuery request, CancellationToken cancellationToken)
    {
        var vehicles = await _vehicleRepository.GetAvailableVehiclesAsync(cancellationToken);
        
        var dtos = vehicles.Select(v => new VehicleDto(
            v.Id,
            v.VehicleName,
            v.VehicleTypeId,
            v.State,
            v.BatteryLevel,
            v.CurrentNodeId)).ToList();

        return Result<IReadOnlyList<VehicleDto>>.Success(dtos.AsReadOnly());
    }
}
