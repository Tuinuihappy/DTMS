using DTMS.SharedKernel.Domain;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Events;

namespace AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;

// Phase 4.1 — Per ADR-016, geofence enforcement is server-strict but
// dispatchers/supervisors can approve an override for the rare legitimate
// case (warehouse GPS drift, address misencoded, operator parked across
// the street, etc.). This aggregate captures the full audit trail —
// who requested, what evidence (photo + GPS coords), who approved/denied
// and why. Expires after the configured window if dispatcher doesn't act.
public class GeofenceOverrideRequest : AggregateRoot<Guid>
{
    public Guid OperatorId { get; private set; }
    public Guid TripId { get; private set; }

    // The leg the operator was trying to complete when the geofence
    // failed — pickup or drop. Stored as the warehouse the operator
    // claimed to be at so the dispatcher can sanity-check the request.
    public Guid ExpectedWarehouseId { get; private set; }
    public double ReportedLatitude { get; private set; }
    public double ReportedLongitude { get; private set; }
    public double DistanceFromGeofenceM { get; private set; }

    // Operator's free-text justification — required at request time so
    // the dispatcher has context. PhotoUrl is optional (POD-style
    // surroundings shot, captured at the same moment as the GPS read).
    public string Reason { get; private set; } = string.Empty;
    public string? PhotoUrl { get; private set; }

    public OverrideRequestStatus Status { get; private set; } = OverrideRequestStatus.Pending;
    public DateTime RequestedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public Guid? DecidedByOperatorId { get; private set; }   // Supervisor / Admin role
    public string? DecisionNote { get; private set; }

    private GeofenceOverrideRequest() { }

    public static GeofenceOverrideRequest Submit(
        Guid operatorId,
        Guid tripId,
        Guid expectedWarehouseId,
        double reportedLat,
        double reportedLng,
        double distanceFromGeofenceM,
        string reason,
        string? photoUrl,
        TimeSpan expiresIn)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("OperatorId must not be empty.", nameof(operatorId));
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId must not be empty.", nameof(tripId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason must not be empty — operator must justify the override.", nameof(reason));
        if (distanceFromGeofenceM <= 0)
            throw new ArgumentException("Distance must be positive (zero means the operator was inside the geofence).", nameof(distanceFromGeofenceM));

        var now = DateTime.UtcNow;
        var req = new GeofenceOverrideRequest
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            TripId = tripId,
            ExpectedWarehouseId = expectedWarehouseId,
            ReportedLatitude = reportedLat,
            ReportedLongitude = reportedLng,
            DistanceFromGeofenceM = distanceFromGeofenceM,
            Reason = reason.Trim(),
            PhotoUrl = photoUrl,
            Status = OverrideRequestStatus.Pending,
            RequestedAt = now,
            ExpiresAt = now.Add(expiresIn),
        };

        req.AddDomainEvent(new GeofenceOverrideRequestedDomainEvent(
            Guid.NewGuid(), now, req.Id, operatorId, tripId, req.Reason));

        return req;
    }

    public void Approve(Guid decidedByOperatorId, string? note = null)
    {
        EnsureDecidable();
        Status = OverrideRequestStatus.Approved;
        DecidedAt = DateTime.UtcNow;
        DecidedByOperatorId = decidedByOperatorId;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        AddDomainEvent(new GeofenceOverrideApprovedDomainEvent(
            Guid.NewGuid(), DecidedAt.Value, Id, decidedByOperatorId));
    }

    public void Deny(Guid decidedByOperatorId, string reason)
    {
        EnsureDecidable();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Denial reason must not be empty.", nameof(reason));

        Status = OverrideRequestStatus.Denied;
        DecidedAt = DateTime.UtcNow;
        DecidedByOperatorId = decidedByOperatorId;
        DecisionNote = reason.Trim();

        AddDomainEvent(new GeofenceOverrideDeniedDomainEvent(
            Guid.NewGuid(), DecidedAt.Value, Id, decidedByOperatorId, DecisionNote));
    }

    // Called by GeofenceOverrideExpiryWatchdog background service —
    // dispatcher took too long, request is auto-rejected so the operator
    // app can prompt for a fresh request.
    public void MarkExpired(DateTime asOf)
    {
        if (Status != OverrideRequestStatus.Pending) return;
        if (asOf < ExpiresAt) return;
        Status = OverrideRequestStatus.Expired;
        DecidedAt = asOf;
        DecisionNote = "Auto-expired — no dispatcher decision within window.";
    }

    private void EnsureDecidable()
    {
        if (Status != OverrideRequestStatus.Pending)
            throw new InvalidOperationException(
                $"Override request {Id} is in terminal state {Status} — cannot change.");
        if (DateTime.UtcNow >= ExpiresAt)
            throw new InvalidOperationException(
                $"Override request {Id} has expired (ExpiresAt={ExpiresAt:O}).");
    }
}
