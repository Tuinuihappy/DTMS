using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.IntegrationEvents;

public record VehicleStateChangedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid VehicleId,
    string State,
    double BatteryLevel,
    Guid? CurrentNodeId) : IIntegrationEvent;
