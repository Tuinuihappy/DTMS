using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;

public class CompleteMaintenanceCommandHandler : ICommandHandler<CompleteMaintenanceCommand>
{
    private readonly IVehicleRepository _vehicleRepo;
    private readonly IMaintenanceRecordRepository _maintenanceRepo;

    public CompleteMaintenanceCommandHandler(
        IVehicleRepository vehicleRepo,
        IMaintenanceRecordRepository maintenanceRepo)
    {
        _vehicleRepo = vehicleRepo;
        _maintenanceRepo = maintenanceRepo;
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

            vehicle.ExitMaintenance();
            _vehicleRepo.Update(vehicle);

            await _vehicleRepo.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
