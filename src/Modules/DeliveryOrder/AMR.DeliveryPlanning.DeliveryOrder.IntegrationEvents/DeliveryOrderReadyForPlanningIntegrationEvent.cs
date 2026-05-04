using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record DeliveryLegDto(Guid LegId, int Sequence, Guid PickupStationId, Guid DropStationId);

public record DeliveryOrderReadyForPlanningIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TenantId,
    Guid DeliveryOrderId,
    string Priority,
    IReadOnlyList<DeliveryLegDto> Legs) : IIntegrationEvent;
