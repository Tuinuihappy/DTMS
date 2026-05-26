using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record ItemHazmatSummaryDto(string ClassCode, string? PackingGroup);

public record ItemTemperatureSummaryDto(double? MinC, double? MaxC);

public record ItemSummaryDto(
    string Sku,
    double WeightKg,
    Guid PickupStationId,
    Guid DropStationId,
    ItemHazmatSummaryDto? Hazmat = null,
    ItemTemperatureSummaryDto? Temperature = null,
    IReadOnlyList<string>? HandlingInstructions = null);

/// <summary>
/// Emitted when a DeliveryOrder enters the Confirmed state — the planning trigger.
/// V1 is the first explicitly-versioned shape; the un-versioned predecessor's
/// <c>Deadline</c> alias (formerly equal to Latest) has been dropped here.
/// Consumers needing the upper window bound should read <see cref="Latest"/>.
/// </summary>
public record DeliveryOrderConfirmedIntegrationEventV1(
    Guid EventId,
    DateTime OccurredOn,
    Guid DeliveryOrderId,
    string Priority,
    string SlaTier,
    DateTime? Earliest,
    DateTime? Latest,
    DateTime? SubmittedAt,
    IReadOnlyList<ItemSummaryDto> Items,
    string SchemaVersion = "1.0") : IIntegrationEvent;
