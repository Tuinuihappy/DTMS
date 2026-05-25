using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record ItemSummaryDto(
    string Sku,
    double WeightKg,
    Guid PickupStationId,
    Guid DropStationId);

public record DeliveryOrderConfirmedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid DeliveryOrderId,
    string Priority,
    string SlaTier,
    DateTime? Earliest,
    DateTime? Latest,
    DateTime? SubmittedAt,
    IReadOnlyList<ItemSummaryDto> Items) : IIntegrationEvent;
