using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.IntegrationEvents;

public record TripStartedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid VehicleId,
    Guid DeliveryOrderId) : IIntegrationEvent;

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

