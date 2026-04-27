using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.IntegrationEvents;

public record TripStartedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid VehicleId) : IIntegrationEvent;

public record TripCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId) : IIntegrationEvent;

public record TripCancelledIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, string Reason) : IIntegrationEvent;

public record ExceptionRaisedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid ExceptionId,
    string Code, string Severity, string Detail) : IIntegrationEvent;

public record PodCapturedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid StopId) : IIntegrationEvent;

// RIOT3 vendor callbacks → Dispatch consumers route these to ReportTaskCompleted/Failed
public record Riot3TaskCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TaskId, string VendorOrderKey) : IIntegrationEvent;

public record Riot3TaskFailedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TaskId, string VendorOrderKey,
    string ErrorCode, string ErrorMessage) : IIntegrationEvent;
