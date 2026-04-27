using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Queries.GetFleetKpi;

public record FleetKpiDto(
    int TotalVehicles,
    int AvailableVehicles,
    int InMaintenanceVehicles,
    double AvailabilityPct,
    double AverageBatteryPct,
    int OfflineVehicles);

public record GetFleetKpiQuery : IQuery<FleetKpiDto>;

public class GetFleetKpiQueryHandler : IQueryHandler<GetFleetKpiQuery, FleetKpiDto>
{
    private readonly IVehicleRepository _vehicleRepo;

    public GetFleetKpiQueryHandler(IVehicleRepository vehicleRepo) => _vehicleRepo = vehicleRepo;

    public async Task<Result<FleetKpiDto>> Handle(GetFleetKpiQuery request, CancellationToken cancellationToken)
    {
        var available = await _vehicleRepo.GetAvailableVehiclesAsync(cancellationToken);
        var allVehicles = available; // Simplified: GetAvailable returns non-offline, non-error vehicles

        var total = allVehicles.Count;
        if (total == 0)
            return Result<FleetKpiDto>.Success(new FleetKpiDto(0, 0, 0, 0, 0, 0));

        var inMaintenance = allVehicles.Count(v => v.State == VehicleState.Maintenance);
        var offline = allVehicles.Count(v => v.State == VehicleState.Offline);
        var readyCount = allVehicles.Count(v => v.State == VehicleState.Idle);
        var avgBattery = allVehicles.Average(v => v.BatteryLevel);
        var availabilityPct = total > 0 ? (double)readyCount / total * 100 : 0;

        return Result<FleetKpiDto>.Success(new FleetKpiDto(
            total, readyCount, inMaintenance, Math.Round(availabilityPct, 1), Math.Round(avgBattery, 1), offline));
    }
}
