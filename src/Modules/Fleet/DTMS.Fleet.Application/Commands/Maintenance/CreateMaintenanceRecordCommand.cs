using DTMS.Fleet.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.Maintenance;

public record CreateMaintenanceRecordCommand(
    Guid VehicleId,
    MaintenanceType Type,
    string Reason,
    string? Technician,
    DateTime ScheduledAt) : ICommand<Guid>;
