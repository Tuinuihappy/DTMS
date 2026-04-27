using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;

public class CreateMaintenanceRecordCommandHandler : ICommandHandler<CreateMaintenanceRecordCommand, Guid>
{
    private readonly IVehicleRepository _vehicleRepo;
    private readonly IMaintenanceRecordRepository _maintenanceRepo;
    private readonly IEventBus _eventBus;

    public CreateMaintenanceRecordCommandHandler(
        IVehicleRepository vehicleRepo,
        IMaintenanceRecordRepository maintenanceRepo,
        IEventBus eventBus)
    {
        _vehicleRepo = vehicleRepo;
        _maintenanceRepo = maintenanceRepo;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(CreateMaintenanceRecordCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await _vehicleRepo.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle == null) return Result<Guid>.Failure($"Vehicle {request.VehicleId} not found.");

        vehicle.EnterMaintenance();
        _vehicleRepo.Update(vehicle);
        await _vehicleRepo.SaveChangesAsync(cancellationToken);

        var record = new Domain.Entities.MaintenanceRecord(
            request.VehicleId, request.Type, request.Reason, request.Technician, request.ScheduledAt);
        await _maintenanceRepo.AddAsync(record, cancellationToken);
        await _maintenanceRepo.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(new VehicleMaintenanceEnteredIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, request.VehicleId, record.Id), cancellationToken);

        return Result<Guid>.Success(record.Id);
    }
}
