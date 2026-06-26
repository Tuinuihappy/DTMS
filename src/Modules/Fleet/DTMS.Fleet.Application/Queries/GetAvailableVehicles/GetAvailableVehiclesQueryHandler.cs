using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetAvailableVehicles;

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
            v.AdapterKey,
            v.VendorVehicleKey,
            v.State,
            v.BatteryLevel,
            v.CurrentNodeId)).ToList();

        return Result<IReadOnlyList<VehicleDto>>.Success(dtos.AsReadOnly());
    }
}
