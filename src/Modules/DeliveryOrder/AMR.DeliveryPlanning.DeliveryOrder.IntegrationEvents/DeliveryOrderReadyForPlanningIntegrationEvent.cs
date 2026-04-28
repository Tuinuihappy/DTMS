using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record DeliveryOrderReadyForPlanningIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TenantId,
    Guid DeliveryOrderId,
    string Priority,
    Guid PickupStationId,
    Guid DropStationId) : IIntegrationEvent;
