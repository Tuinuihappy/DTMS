using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.IntegrationEvents;

public record JobAssignedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid VehicleId,
    Guid PickupStationId,
    Guid DropStationId) : IIntegrationEvent;

public record PlannedLegDto(
    Guid FromStationId,
    Guid ToStationId,
    int SequenceOrder);

public record PlanCommittedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid? VehicleId,
    List<PlannedLegDto> Legs) : IIntegrationEvent;

// ── Phase P1 (b12) — Job status lifecycle integration events ───────────
// These bridge the existing JobXxxDomainEvent stream to anything that
// wants to react to a Job state change without coupling to the Planning
// domain assembly. The projector under Phase P1 is the first consumer.
// SchemaVersion follows the same convention as DeliveryOrder events:
// additive fields bump minor; breaking changes bump the class name.

public record JobCreatedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record JobDispatchedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId, string? VendorOrderKey, int AttemptNumber,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record JobExecutingIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record JobCompletedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record JobFailedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    string Reason, int AttemptNumber,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record JobCancelledIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId, string Reason,
    string SchemaVersion = "1.0") : IIntegrationEvent;
