using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.IntegrationEvents;

public record TripStartedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid VehicleId,
    Guid DeliveryOrderId) : IIntegrationEvent;

public record TripPickupCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId) : IIntegrationEvent;

public record TripDropCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId) : IIntegrationEvent;

// VendorUpperKey is the composite envelope correlation key
// (see EnvelopeUpperKey) that RIOT3 echoes back on every webhook.
public record TripCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId,
    string VendorUpperKey) : IIntegrationEvent;

public record TripFailedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId,
    string Reason, string VendorUpperKey) : IIntegrationEvent;

public record TripCancelledIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId, string Reason, string? VendorUpperKey) : IIntegrationEvent;

public record ExceptionRaisedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid ExceptionId,
    string Code, string Severity, string Detail) : IIntegrationEvent;

public record PodCapturedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TripId,
    Guid StopId,
    IReadOnlyList<string> ScannedIds) : IIntegrationEvent;

// Phase P1 (b12) — pause/resume transitions surface to the projector so
// the Trip status timeline covers every state in the TripStatus enum
// (Created/InProgress/Paused/Completed/Failed/Cancelled). No existing
// consumer reacts to these — only TripStatusHistoryProjector does today.
public record TripPausedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid TripId,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record TripResumedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid TripId,
    string SchemaVersion = "1.0") : IIntegrationEvent;

