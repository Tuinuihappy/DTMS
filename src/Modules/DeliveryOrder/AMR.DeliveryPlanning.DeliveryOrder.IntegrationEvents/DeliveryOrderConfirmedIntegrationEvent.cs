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
    // Backward-compat alias for Latest. Existing v1 consumers may read `Deadline`;
    // new consumers should use Earliest+Latest. Slated for removal in P1-8.
    DateTime? Deadline,
    DateTime? SubmittedAt,
    IReadOnlyList<ItemSummaryDto> Items) : IIntegrationEvent;
