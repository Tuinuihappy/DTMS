using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Transport.Manual.Domain.Events;

public record OperatorRegisteredDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OperatorId,
    string EmployeeCode) : IDomainEvent;

public record OperatorAssignedToTripDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OperatorId,
    Guid TripId) : IDomainEvent;

public record OperatorReleasedFromTripDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OperatorId,
    Guid TripId) : IDomainEvent;

public record OperatorWentOnLeaveDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OperatorId,
    string Reason) : IDomainEvent;

public record OperatorDeactivatedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OperatorId,
    string Reason) : IDomainEvent;

// Geofence override flow (per ADR-016).
public record GeofenceOverrideRequestedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid RequestId,
    Guid OperatorId,
    Guid TripId,
    string Reason) : IDomainEvent;

public record GeofenceOverrideApprovedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid RequestId,
    Guid ApprovedByOperatorId) : IDomainEvent;

public record GeofenceOverrideDeniedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid RequestId,
    Guid DeniedByOperatorId,
    string Reason) : IDomainEvent;
