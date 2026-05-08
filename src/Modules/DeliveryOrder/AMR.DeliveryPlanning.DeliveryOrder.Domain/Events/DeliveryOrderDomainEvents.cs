using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;

public record PackageSummaryEventDto(string Barcode, string LoadUnitProfileCode, double GrossWeightKg);

public record DeliveryLegEventDto(
    Guid LegId,
    int Sequence,
    Guid PickupStationId,
    Guid DropStationId,
    string CarrierTypeCode,
    int TotalPackageCount,
    double TotalWeight,
    IReadOnlyList<PackageSummaryEventDto> Packages);

public record DeliveryOrderDraftedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderSubmittedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderValidatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid TenantId, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReadyToPlanDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TenantId,
    Guid OrderId,
    string SlaTier,
    DateTime? ServiceWindowEarliest,
    DateTime? ServiceWindowLatest,
    IReadOnlyList<DeliveryLegEventDto> Legs) : IDomainEvent;
public record DeliveryOrderPlanningStartedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderPlannedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderDispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderInProgressDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderHeldDomainEvent(Guid EventId, DateTime OccurredOn, Guid TenantId, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReleasedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TenantId, Guid OrderId) : IDomainEvent;
public record DeliveryOrderFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TenantId, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderAmendedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TenantId, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TenantId, Guid OrderId) : IDomainEvent;
