using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;

public record CompleteMaintenanceCommand(Guid VehicleId, Guid MaintenanceRecordId, string Outcome) : ICommand;
