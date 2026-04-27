using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;

public class CompleteMaintenanceCommandHandler : ICommandHandler<CompleteMaintenanceCommand>
{
    private readonly IVehicleRepository _vehicleRepo;
    private readonly IMaintenanceRecordRepository _maintenanceRepo;
    private readonly IEventBus _eventBus;

    public CompleteMaintenanceCommandHandler(
        IVehicleRepository vehicleRepo,
        IMaintenanceRecordRepository maintenanceRepo,
        IEventBus eventBus)
    {
        _vehicleRepo = vehicleRepo;
        _maintenanceRepo = maintenanceRepo;
        _eventBus = eventBus;
    }

    public async Task<Result> Handle(CompleteMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await _vehicleRepo.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle == null) return Result.Failure($"Vehicle {request.VehicleId} not found.");

        var record = await _maintenanceRepo.GetByIdAsync(request.MaintenanceRecordId, cancellationToken);
        if (record == null) return Result.Failure($"Maintenance record {request.MaintenanceRecordId} not found.");

        try
        {
            record.Complete(request.Outcome);
            await _maintenanceRepo.UpdateAsync(record, cancellationToken);
            await _maintenanceRepo.SaveChangesAsync(cancellationToken);

            vehicle.ExitMaintenance();
            _vehicleRepo.Update(vehicle);
            await _vehicleRepo.SaveChangesAsync(cancellationToken);

            await _eventBus.PublishAsync(new VehicleMaintenanceExitedIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, request.VehicleId), cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
