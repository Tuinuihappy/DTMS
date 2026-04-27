using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;

public class VendorAdapterTaskDispatcher : ITaskDispatcher
{
    private readonly IVendorAdapterFactory _adapterFactory;
    private readonly ILogger<VendorAdapterTaskDispatcher> _logger;

    public VendorAdapterTaskDispatcher(IVendorAdapterFactory adapterFactory, ILogger<VendorAdapterTaskDispatcher> logger)
    {
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid vehicleId, RobotTask task, CancellationToken cancellationToken = default)
    {
        var adapter = _adapterFactory.GetAdapterForVehicle(vehicleId);
        var command = MapToVendorCommand(task);
        var result = await adapter.SendTaskAsync(vehicleId, command, cancellationToken);
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

    private static RobotTaskCommand MapToVendorCommand(RobotTask task)
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

        return new RobotTaskCommand
        {
            TaskId = task.Id,
            Action = action,
            TargetNodeId = task.TargetStationId?.ToString()
        };
    }
}
