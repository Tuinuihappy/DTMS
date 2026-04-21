using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.IntegrationEvents;

public record TripStartedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid VehicleId) : IIntegrationEvent;

public record TripCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId) : IIntegrationEvent;
