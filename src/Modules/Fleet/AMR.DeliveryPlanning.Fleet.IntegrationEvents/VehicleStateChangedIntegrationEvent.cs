using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.IntegrationEvents;

public record VehicleStateChangedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid VehicleId,
    string State,
    double BatteryLevel,
    Guid? CurrentNodeId) : IIntegrationEvent;

public record VehicleBatteryLowIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid VehicleId,
    Guid VehicleTypeId,
    double BatteryLevel) : IIntegrationEvent;

public record VehicleMaintenanceEnteredIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid VehicleId,
    Guid MaintenanceRecordId) : IIntegrationEvent;

public record VehicleMaintenanceExitedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid VehicleId) : IIntegrationEvent;
