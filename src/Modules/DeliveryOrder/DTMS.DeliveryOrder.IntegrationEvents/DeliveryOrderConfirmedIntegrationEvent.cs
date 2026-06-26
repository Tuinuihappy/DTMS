using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record ItemHazmatSummaryDto(string ClassCode, string? PackingGroup);

public record ItemTemperatureSummaryDto(double? MinC, double? MaxC);

// Phase 3a (multi-mode): nullable station Ids + new warehouse Ids
// mirror ItemEventDto. Manual / Fleet orders carry warehouse Ids
// only; AMR carries station Ids only. Consumers (Planning) dispatch
// on RequestedTransportMode to pick the right pair.
public record ItemSummaryDto(
    string ItemId,
    double WeightKg,
    Guid? PickupStationId,
    Guid? DropStationId,
    ItemHazmatSummaryDto? Hazmat = null,
    ItemTemperatureSummaryDto? Temperature = null,
    IReadOnlyList<string>? HandlingInstructions = null,
    Guid? PickupWarehouseId = null,
    Guid? DropWarehouseId = null);

/// <summary>
/// Emitted when a DeliveryOrder enters the Confirmed state — the planning trigger.
/// V1 is the first explicitly-versioned shape.
/// </summary>
public record DeliveryOrderConfirmedIntegrationEventV1(
    Guid EventId,
    DateTime OccurredOn,
    Guid DeliveryOrderId,
    string Priority,
    DateTime? EarliestUtc,
    DateTime? LatestUtc,
    DateTime? SubmittedAt,
    IReadOnlyList<ItemSummaryDto> Items,
    string? RequestedTransportMode = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;
