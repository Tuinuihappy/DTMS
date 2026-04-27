using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;

public record CreateMaintenanceRecordCommand(
    Guid VehicleId,
    MaintenanceType Type,
    string Reason,
    string? Technician,
    DateTime ScheduledAt) : ICommand<Guid>;
