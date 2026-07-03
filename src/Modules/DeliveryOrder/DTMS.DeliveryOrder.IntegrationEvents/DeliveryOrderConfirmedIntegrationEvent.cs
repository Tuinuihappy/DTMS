using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.IntegrationEvents;

public record ItemHazmatSummaryDto(string ClassCode, string? PackingGroup);

public record ItemTemperatureSummaryDto(double? MinC, double? MaxC);

// WMS PR-2 — station Ids for AMR, WMS location Ids for Manual/Fleet.
// Both pairs nullable so Consumers (Planning) can dispatch on the
// RequestedTransportMode to pick the right pair.
public record ItemSummaryDto(
    string ItemId,
    double WeightKg,
    Guid? PickupStationId,
    Guid? DropStationId,
    ItemHazmatSummaryDto? Hazmat = null,
    ItemTemperatureSummaryDto? Temperature = null,
    IReadOnlyList<string>? HandlingInstructions = null,
    Guid? PickupWmsLocationId = null,
    Guid? DropWmsLocationId = null);

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
