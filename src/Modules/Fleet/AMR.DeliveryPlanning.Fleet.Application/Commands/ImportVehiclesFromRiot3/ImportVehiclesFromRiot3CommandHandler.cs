using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.ImportVehiclesFromRiot3;

internal sealed class ImportVehiclesFromRiot3CommandHandler
    : ICommandHandler<ImportVehiclesFromRiot3Command, ImportVehiclesFromRiot3Result>
{
    private readonly IRiot3FleetClient _riot3;
    private readonly IFleetReadService _fleetReadService;
    private readonly IVehicleRepository _vehicleRepository;
    private readonly IVehicleTypeRepository _vehicleTypeRepository;
    private readonly ILogger<ImportVehiclesFromRiot3CommandHandler> _logger;

    public ImportVehiclesFromRiot3CommandHandler(
        IRiot3FleetClient riot3,
        IFleetReadService fleetReadService,
        IVehicleRepository vehicleRepository,
        IVehicleTypeRepository vehicleTypeRepository,
        ILogger<ImportVehiclesFromRiot3CommandHandler> logger)
    {
        _riot3 = riot3;
        _fleetReadService = fleetReadService;
        _vehicleRepository = vehicleRepository;
        _vehicleTypeRepository = vehicleTypeRepository;
        _logger = logger;
    }

    public async Task<Result<ImportVehiclesFromRiot3Result>> Handle(
        ImportVehiclesFromRiot3Command request, CancellationToken cancellationToken)
    {
        var robots = await _riot3.GetAllRobotsAsync(cancellationToken);
        if (robots.Count == 0)
            return Result<ImportVehiclesFromRiot3Result>.Failure("No robots returned from RIOT3.");

        var details = new List<ImportedVehicleDetail>();
        int imported = 0, skipped = 0;

        foreach (var robot in robots)
        {
            var existingId = await _fleetReadService.ResolveVehicleIdAsync(
                "riot3", robot.DeviceKey, cancellationToken);

            if (existingId.HasValue)
            {
                _logger.LogDebug("Skipping {DeviceKey} ({Name}) — already registered", robot.DeviceKey, robot.DeviceName);
                details.Add(new ImportedVehicleDetail(robot.DeviceKey, robot.DeviceName, "skipped - already exists", existingId));
                skipped++;
                continue;
            }

            var vehicleTypeId = ResolveVehicleTypeId(robot.TypeKey, request);
            if (vehicleTypeId == Guid.Empty)
            {
                _logger.LogWarning("Skipping {DeviceKey} — no VehicleTypeId mapping for typeKey '{TypeKey}'", robot.DeviceKey, robot.TypeKey);
                details.Add(new ImportedVehicleDetail(robot.DeviceKey, robot.DeviceName, $"skipped - no mapping for typeKey '{robot.TypeKey}'"));
                skipped++;
                continue;
            }

            var vehicleType = await _vehicleTypeRepository.GetByIdAsync(vehicleTypeId, cancellationToken);
            if (vehicleType is null)
            {
                _logger.LogWarning("Skipping {DeviceKey} — VehicleTypeId {TypeId} not found", robot.DeviceKey, vehicleTypeId);
                details.Add(new ImportedVehicleDetail(robot.DeviceKey, robot.DeviceName, $"skipped - VehicleTypeId {vehicleTypeId} not found"));
                skipped++;
                continue;
            }

            var vehicle = new Vehicle(Guid.NewGuid(), robot.DeviceName, vehicleTypeId,
                adapterKey: "riot3", vendorVehicleKey: robot.DeviceKey);

            var state = MapVehicleState(robot.ConnectionState, robot.SystemState);
            vehicle.UpdateState(state, robot.BatteryPercentage, currentNodeId: null);

            await _vehicleRepository.AddAsync(vehicle, cancellationToken);
            await _vehicleRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Imported vehicle {Name} ({DeviceKey}) as {VehicleId}", robot.DeviceName, robot.DeviceKey, vehicle.Id);
            details.Add(new ImportedVehicleDetail(robot.DeviceKey, robot.DeviceName, "imported", vehicle.Id));
            imported++;
        }

        return Result<ImportVehiclesFromRiot3Result>.Success(
            new ImportVehiclesFromRiot3Result(imported, skipped, details));
    }

    private static Guid ResolveVehicleTypeId(string typeKey, ImportVehiclesFromRiot3Command request)
    {
        if (request.TypeKeyMappings.TryGetValue(typeKey, out var mapped))
            return mapped;

        return request.DefaultVehicleTypeId ?? Guid.Empty;
    }

    private static VehicleState MapVehicleState(string connectionState, string systemState)
    {
        if (connectionState is not "ONLINE")
            return VehicleState.Offline;

        return systemState switch
        {
            "IDLE" or "NONE" => VehicleState.Idle,
            "EXECUTING"      => VehicleState.Moving,
            "CHARGING"       => VehicleState.Charging,
            "ERROR"          => VehicleState.Error,
            _                => VehicleState.Offline,
        };
    }
}
