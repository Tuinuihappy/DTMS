using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;

public class CreateMaintenanceRecordCommandHandler : ICommandHandler<CreateMaintenanceRecordCommand, Guid>
{
    private readonly IVehicleRepository _vehicleRepo;
    private readonly IMaintenanceRecordRepository _maintenanceRepo;

    public CreateMaintenanceRecordCommandHandler(
        IVehicleRepository vehicleRepo,
        IMaintenanceRecordRepository maintenanceRepo)
    {
        _vehicleRepo = vehicleRepo;
        _maintenanceRepo = maintenanceRepo;
    }

    public async Task<Result<Guid>> Handle(CreateMaintenanceRecordCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await _vehicleRepo.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle == null) return Result<Guid>.Failure($"Vehicle {request.VehicleId} not found.");

        var record = new Domain.Entities.MaintenanceRecord(
            request.VehicleId, request.Type, request.Reason, request.Technician, request.ScheduledAt);

        vehicle.EnterMaintenance(record.Id);
        _vehicleRepo.Update(vehicle);
        await _maintenanceRepo.AddAsync(record, cancellationToken);

        await _vehicleRepo.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(record.Id);
    }
}
