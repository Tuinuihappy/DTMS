using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;

public class VendorAdapterTaskDispatcher : ITaskDispatcher
{
    private readonly IVendorAdapterFactory _adapterFactory;
    private readonly FacilityDbContext _facilityDbContext;
    private readonly ILogger<VendorAdapterTaskDispatcher> _logger;

    public VendorAdapterTaskDispatcher(
        IVendorAdapterFactory adapterFactory,
        FacilityDbContext facilityDbContext,
        ILogger<VendorAdapterTaskDispatcher> logger)
    {
        _adapterFactory = adapterFactory;
        _facilityDbContext = facilityDbContext;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid vehicleId, RobotTask task, CancellationToken cancellationToken = default)
    {
        var resolution = _adapterFactory.GetAdapterResolutionForVehicle(vehicleId);
        var command = await MapToVendorCommandAsync(task, resolution.AdapterKey, cancellationToken);
        if (command is null)
        {
            _logger.LogError("Task {TaskId} for vehicle {VehicleId} was not sent because vendor target mapping is incomplete",
                task.Id, vehicleId);
            return;
        }

        command.VendorVehicleKey = resolution.VendorVehicleKey;

        var result = await resolution.Adapter.SendTaskAsync(vehicleId, command, cancellationToken);
        if (!result.IsSuccess)
            _logger.LogWarning("Vendor rejected task {TaskId} for vehicle {VehicleId}: {Error}", task.Id, vehicleId, result.Error);
    }

    public async Task CancelAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var adapter = _adapterFactory.GetAdapterForVehicle(vehicleId);
        var result = await adapter.CancelTaskAsync(vehicleId, taskId, cancellationToken);
        if (!result.IsSuccess)
            _logger.LogWarning("Failed to cancel task {TaskId} for vehicle {VehicleId}: {Error}", taskId, vehicleId, result.Error);
    }

    public async Task PauseAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var adapter = _adapterFactory.GetAdapterForVehicle(vehicleId);
        var result = await adapter.PauseTaskAsync(vehicleId, taskId, cancellationToken);
        if (!result.IsSuccess)
            _logger.LogWarning("Failed to pause task {TaskId} for vehicle {VehicleId}: {Error}", taskId, vehicleId, result.Error);
    }

    public async Task ResumeAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var adapter = _adapterFactory.GetAdapterForVehicle(vehicleId);
        var result = await adapter.ResumeTaskAsync(vehicleId, taskId, cancellationToken);
        if (!result.IsSuccess)
            _logger.LogWarning("Failed to resume task {TaskId} for vehicle {VehicleId}: {Error}", taskId, vehicleId, result.Error);
    }

    private async Task<RobotTaskCommand?> MapToVendorCommandAsync(
        RobotTask task,
        string adapterKey,
        CancellationToken cancellationToken)
    {
        var action = task.Type switch
        {
            TaskType.Move => RobotActionType.MOVE,
            TaskType.Lift => RobotActionType.LIFT,
            TaskType.Drop => RobotActionType.DROP,
            TaskType.Charge => RobotActionType.CHARGE,
            TaskType.Park => RobotActionType.PARK,
            _ => RobotActionType.MOVE
        };

        var command = new RobotTaskCommand
        {
            TaskId = task.Id,
            Action = action,
            TargetNodeId = task.TargetStationId?.ToString()
        };

        if (!RequiresRiot3VendorRefs(adapterKey, action))
        {
            return command;
        }

        if (!task.TargetStationId.HasValue)
        {
            _logger.LogError("RIOT3 task {TaskId} ({Action}) has no target station", task.Id, action);
            return null;
        }

        var station = await _facilityDbContext.Stations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == task.TargetStationId.Value, cancellationToken);
        if (station is null)
        {
            _logger.LogError("RIOT3 task {TaskId} target station {StationId} was not found",
                task.Id, task.TargetStationId.Value);
            return null;
        }

        var map = await _facilityDbContext.Maps
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == station.MapId, cancellationToken);
        if (map is null)
        {
            _logger.LogError("RIOT3 task {TaskId} map {MapId} for station {StationId} was not found",
                task.Id, station.MapId, station.Id);
            return null;
        }

        if (string.IsNullOrWhiteSpace(station.VendorRef) || string.IsNullOrWhiteSpace(map.VendorRef))
        {
            _logger.LogError(
                "RIOT3 task {TaskId} requires vendor refs but map {MapId} VendorRef={MapVendorRef} station {StationId} VendorRef={StationVendorRef}",
                task.Id, map.Id, map.VendorRef, station.Id, station.VendorRef);
            return null;
        }

        command.MapId = map.VendorRef.Trim();
        command.TargetNodeId = station.VendorRef.Trim();
        return command;
    }

    private static bool RequiresRiot3VendorRefs(string adapterKey, RobotActionType action)
    {
        if (action is not (RobotActionType.MOVE or RobotActionType.CHARGE))
        {
            return false;
        }

        return adapterKey.Equals("riot3", StringComparison.OrdinalIgnoreCase)
            || adapterKey.Equals("feeder", StringComparison.OrdinalIgnoreCase);
    }
}
