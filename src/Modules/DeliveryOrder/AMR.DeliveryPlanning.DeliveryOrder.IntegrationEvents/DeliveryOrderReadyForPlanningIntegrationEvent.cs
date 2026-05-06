using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record PackageSummaryDto(string Barcode, string LoadUnitProfileCode, double GrossWeightKg);

public record DeliveryLegDto(
    Guid LegId,
    int Sequence,
    Guid PickupStationId,
    Guid DropStationId,
    string CarrierTypeCode,
    int TotalPackageCount,
    double TotalWeight,
    IReadOnlyList<PackageSummaryDto> Packages);

public record DeliveryOrderReadyForPlanningIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TenantId,
    Guid DeliveryOrderId,
    string SlaTier,
    IReadOnlyList<DeliveryLegDto> Legs) : IIntegrationEvent;
