using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;

public class VendorAdapterTaskDispatcher : ITaskDispatcher
{
    private readonly IVendorAdapterFactory _adapterFactory;
    private readonly IFacilityReadService _facilityReadService;
    private readonly ILogger<VendorAdapterTaskDispatcher> _logger;

    public VendorAdapterTaskDispatcher(
        IVendorAdapterFactory adapterFactory,
        IFacilityReadService facilityReadService,
        ILogger<VendorAdapterTaskDispatcher> logger)
    {
        _adapterFactory = adapterFactory;
        _facilityReadService = facilityReadService;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid vehicleId, RobotTask task, CancellationToken cancellationToken = default)
    {
        var resolution = await _adapterFactory.GetAdapterResolutionForVehicleAsync(vehicleId, cancellationToken);
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
        var adapter = await _adapterFactory.GetAdapterForVehicleAsync(vehicleId, cancellationToken);
        var result = await adapter.CancelTaskAsync(vehicleId, taskId, cancellationToken);
        if (!result.IsSuccess)
            _logger.LogWarning("Failed to cancel task {TaskId} for vehicle {VehicleId}: {Error}", taskId, vehicleId, result.Error);
    }

    public async Task PauseAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var adapter = await _adapterFactory.GetAdapterForVehicleAsync(vehicleId, cancellationToken);
        var result = await adapter.PauseTaskAsync(vehicleId, taskId, cancellationToken);
        if (!result.IsSuccess)
            _logger.LogWarning("Failed to pause task {TaskId} for vehicle {VehicleId}: {Error}", taskId, vehicleId, result.Error);
    }

    public async Task ResumeAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var adapter = await _adapterFactory.GetAdapterForVehicleAsync(vehicleId, cancellationToken);
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

        var vendorTarget = await _facilityReadService.GetStationVendorTargetAsync(
            task.TargetStationId.Value,
            cancellationToken);
        if (vendorTarget is null)
        {
            _logger.LogError(
                "RIOT3 task {TaskId} requires a station with configured map/station vendor refs; station {StationId}",
                task.Id, task.TargetStationId.Value);
            return null;
        }

        command.MapId = vendorTarget.MapVendorRef;
        command.TargetNodeId = vendorTarget.StationVendorRef;
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
