using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Events;

// VendorVehicleKey carries the deviceKey RIOT3 echoes on TASK_PROCESSING
// (already captured on the Trip aggregate by the time this event fires).
// Nullable because pre-vendor-key trips and tests may not set it.
//
// Items (Phase P5.3) — snapshot of items bound to the trip, supplied by
// the caller of MarkVendorStarted via ITripItemSnapshotProvider. Domain
// event carries the same shape as the integration event so the mapper is
// a 1:1 pass-through. Null/empty means "no item context available" —
// not an error.
public record TripStartedDomainEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId,
    Guid? VehicleId, string? VendorVehicleKey,
    IReadOnlyList<TripItemSnapshot>? Items = null) : IDomainEvent;
public record TripPickupCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId) : IDomainEvent;

// RequiresDropPod is the order's effective POD policy resolved at
// drop-completion time. False (or null with template-default false)
// → DeliveryOrder consumer lands items at Delivered directly and the
// TripItems projector skips the "DroppedOff" interstitial. True →
// items hold at DroppedOff until /pod-scan completes. Nullable so
// pre-rollout in-flight events still process safely (consumers fall
// back to reading Order.RequiresDropPod when null).
public record TripDropCompletedDomainEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId,
    bool? RequiresDropPod = null) : IDomainEvent;

// VendorUpperKey is the composite envelope correlation key
// (see EnvelopeUpperKey) that RIOT3 echoes back on every webhook.
public record TripCompletedDomainEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId,
    string VendorUpperKey) : IDomainEvent;

public record TripFailedDomainEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId,
    string Reason, string VendorUpperKey) : IDomainEvent;

public record TripPausedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId) : IDomainEvent;
public record TripResumedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId) : IDomainEvent;

// Operator confirmed a robot waiting at a checkpoint may proceed (RIOT3 PASS).
// Trip.Status is intentionally unchanged — this is an interactive nudge at
// robot level, not a state transition. VendorVehicleKey is the deviceKey
// that was acknowledged (carried for audit + projector display).
public record TripRobotPassAcknowledgedDomainEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, string VendorVehicleKey) : IDomainEvent;
public record TripCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId, string Reason, string? VendorUpperKey) : IDomainEvent;

// Emitted the first time a vehicle is bound to a trip that was created
// without one (e.g. RIOT3 auto-selected the robot and reported it back
// via processingVehicle.key on the first task webhook).
public record TripVehicleAssignedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid VehicleId) : IDomainEvent;

public record ExceptionRaisedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TripId,
    Guid JobId,
    Guid ExceptionId,
    string Code,
    string Severity,
    string Detail) : IDomainEvent;
public record ExceptionResolvedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid ExceptionId, string Resolution) : IDomainEvent;
public record PodCapturedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TripId,
    Guid StopId,
    IReadOnlyList<string> ScannedIds) : IDomainEvent;
