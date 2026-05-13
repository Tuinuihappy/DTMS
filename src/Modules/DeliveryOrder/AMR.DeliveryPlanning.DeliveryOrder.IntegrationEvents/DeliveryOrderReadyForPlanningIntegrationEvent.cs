using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record ItemSummaryDto(
    string Sku,
    double WeightKg,
    Guid PickupStationId,
    Guid DropStationId);

public record DeliveryOrderReadyForPlanningIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid DeliveryOrderId,
    string Priority,
    DateTime? Deadline,
    IReadOnlyList<ItemSummaryDto> Items) : IIntegrationEvent;
