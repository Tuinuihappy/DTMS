using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.IntegrationEvents;

public record JobAssignedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid VehicleId,
    Guid PickupStationId,
    Guid DropStationId) : IIntegrationEvent;

public record PlannedLegDto(
    Guid FromStationId,
    Guid ToStationId,
    int SequenceOrder);

public record PlanCommittedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TenantId,
    Guid JobId,
    Guid? VehicleId,
    List<PlannedLegDto> Legs) : IIntegrationEvent;
