using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Events;

namespace AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;

// Phase 4.1 — Operator aggregate root for the Manual transport mode.
// Per ADR-014, identity is owned by External Auth (10.204.212.28:15000);
// DTMS stores only Operator-specific data (warehouse scope, certifications,
// push subscriptions, current trip binding). Operator rows are created
// on demand by OperatorSyncMiddleware the first time an employee logs in
// with role=Operator; subsequent logins refresh DisplayName + Role from
// the JWT claims (External Auth is source of truth for those fields).
//
// EmployeeCode is the natural key — matches External Auth's user
// identifier. Id is DTMS's internal Guid for FK use in other tables
// (OperatorPushSubscription, GeofenceOverrideRequest, etc.).
public class Operator : AggregateRoot<Guid>
{
    // External Auth user identifier — the canonical reference for this
    // operator across systems. Unique within DTMS.
    public string EmployeeCode { get; private set; } = string.Empty;

    // Synced from JWT claims on each login. External Auth is source of
    // truth — DTMS overwrites local copy when the value drifts.
    public string DisplayName { get; private set; } = string.Empty;
    public OperatorRole Role { get; private set; }

    // DTMS-owned lifecycle. Active = available for assignment;
    // OnLeave / Deactivated stop new trip assignments but don't disturb
    // an in-flight trip already bound to this operator.
    public OperatorStatus Status { get; private set; } = OperatorStatus.Active;

    // Optional default-warehouse hint — dispatcher console uses this to
    // pre-filter the operator picker. Null = operator can serve any
    // warehouse (typically a "floating" worker covering multiple sites).
    public Guid? PrimaryWarehouseId { get; private set; }

    // Currently-assigned trip — at most one active trip per operator
    // (enforced by AssignToTrip below). Null when idle / off-shift.
    public Guid? CurrentTripId { get; private set; }

    // Contact / display data — populated from External Auth or via
    // dispatcher console updates (phone number isn't in External Auth
    // typically). All optional — operator workflow doesn't depend on them.
    public string? Phone { get; private set; }
    public string? ThumbnailUrl { get; private set; }

    // Audit timestamps — Created on first sync, LastSyncedAt every login.
    public DateTime CreatedAt { get; private set; }
    public DateTime LastSyncedAt { get; private set; }

    // Certifications + push subscriptions stored as separate entities
    // (1:N relationship). Loaded via EF Include when needed; the aggregate
    // exposes read-only views over the private backing lists.
    private readonly List<OperatorCertification> _certifications = new();
    public IReadOnlyCollection<OperatorCertification> Certifications => _certifications.AsReadOnly();

    private readonly List<OperatorPushSubscription> _pushSubscriptions = new();
    public IReadOnlyCollection<OperatorPushSubscription> PushSubscriptions => _pushSubscriptions.AsReadOnly();

    private Operator() { }

    // Factory — used by OperatorSyncMiddleware on first login. The
    // employee already exists in External Auth (JWT validated upstream);
    // this just registers their DTMS-side presence.
    public static Operator CreateFromJwtClaims(
        string employeeCode,
        string displayName,
        OperatorRole role,
        Guid? primaryWarehouseId = null,
        string? phone = null,
        string? thumbnailUrl = null)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new ArgumentException("EmployeeCode must not be empty.", nameof(employeeCode));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));

        var now = DateTime.UtcNow;
        var op = new Operator
        {
            Id = Guid.NewGuid(),
            EmployeeCode = employeeCode.Trim(),
            DisplayName = displayName.Trim(),
            Role = role,
            Status = OperatorStatus.Active,
            PrimaryWarehouseId = primaryWarehouseId,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? null : thumbnailUrl,
            CreatedAt = now,
            LastSyncedAt = now,
        };

        op.AddDomainEvent(new OperatorRegisteredDomainEvent(
            Guid.NewGuid(), now, op.Id, op.EmployeeCode));

        return op;
    }

    // Per-login sync — External Auth claims are source of truth for
    // DisplayName + Role. Phone / ThumbnailUrl optional; only overwrite
    // when caller supplies them (operator may have local override in DTMS).
    public void SyncFromJwtClaims(
        string displayName,
        OperatorRole role,
        string? thumbnailUrl = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));

        DisplayName = displayName.Trim();
        Role = role;
        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
            ThumbnailUrl = thumbnailUrl;
        LastSyncedAt = DateTime.UtcNow;
    }

    // Dispatcher console — set operator's home warehouse (for picker
    // filtering and SLA reports). Idempotent — same warehouse twice is
    // a no-op and doesn't fire a domain event.
    public void SetPrimaryWarehouse(Guid? warehouseId)
    {
        if (PrimaryWarehouseId == warehouseId) return;
        PrimaryWarehouseId = warehouseId;
    }

    // Assign trip — caller (ManualDispatchStrategy when it ships in
    // Phase 4.4) guards on Status == Active and Certifications.
    // Enforces single-active-trip invariant.
    public void AssignToTrip(Guid tripId)
    {
        if (Status != OperatorStatus.Active)
            throw new InvalidOperationException(
                $"Operator '{EmployeeCode}' is not active (Status={Status}) — cannot assign trip.");
        if (CurrentTripId.HasValue && CurrentTripId.Value != tripId)
            throw new InvalidOperationException(
                $"Operator '{EmployeeCode}' is already assigned to trip {CurrentTripId.Value}. " +
                "Complete or unassign before reassigning.");

        if (CurrentTripId == tripId) return;   // idempotent re-assign
        CurrentTripId = tripId;
        AddDomainEvent(new OperatorAssignedToTripDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, tripId));
    }

    // Trip handoff back — called when trip terminates (Completed /
    // Failed / Cancelled). Clears assignment so operator becomes
    // available for next work. Safe to call on already-cleared state.
    public void ClearTripAssignment()
    {
        if (CurrentTripId is null) return;
        var releasedTripId = CurrentTripId.Value;
        CurrentTripId = null;
        AddDomainEvent(new OperatorReleasedFromTripDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, releasedTripId));
    }

    // Lifecycle state changes — dispatcher console actions.
    public void GoOnLeave(string reason)
    {
        if (Status == OperatorStatus.OnLeave) return;
        if (Status == OperatorStatus.Deactivated)
            throw new InvalidOperationException("Cannot put a deactivated operator on leave.");
        if (CurrentTripId.HasValue)
            throw new InvalidOperationException(
                "Cannot go on leave with an active trip — complete or unassign first.");

        Status = OperatorStatus.OnLeave;
        AddDomainEvent(new OperatorWentOnLeaveDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void ReturnFromLeave()
    {
        if (Status == OperatorStatus.Active) return;
        if (Status == OperatorStatus.Deactivated)
            throw new InvalidOperationException("Cannot reactivate a deactivated operator — re-register instead.");

        Status = OperatorStatus.Active;
    }

    public void Deactivate(string reason)
    {
        if (Status == OperatorStatus.Deactivated) return;
        if (CurrentTripId.HasValue)
            throw new InvalidOperationException(
                "Cannot deactivate with an active trip — complete or reassign first.");

        Status = OperatorStatus.Deactivated;
        AddDomainEvent(new OperatorDeactivatedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    // Certification management — dispatcher console / supervisor actions.
    public void AddCertification(CertificationType type, DateTime? expiresAt)
    {
        if (_certifications.Any(c => c.Type == type && c.IsActive))
            return;   // idempotent — already certified
        _certifications.Add(OperatorCertification.Create(Id, type, expiresAt));
    }

    public void RevokeCertification(CertificationType type, string reason)
    {
        var cert = _certifications.FirstOrDefault(c => c.Type == type && c.IsActive);
        cert?.Revoke(reason);
    }

    // Push subscription management — called by /api/operator/devices/register-push.
    public void RegisterPushSubscription(
        PushPlatform platform, string endpoint,
        string? publicKey, string? authSecret,
        string? deviceLabel)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        // Replace any existing subscription with the same endpoint
        // (browser may rotate URLs; same device → one row).
        var existing = _pushSubscriptions.FirstOrDefault(s => s.Endpoint == endpoint);
        if (existing is not null)
        {
            existing.UpdateKeys(publicKey, authSecret, deviceLabel);
            return;
        }
        _pushSubscriptions.Add(OperatorPushSubscription.Create(
            Id, platform, endpoint, publicKey, authSecret, deviceLabel));
    }

    public void RemovePushSubscription(string endpoint)
    {
        var sub = _pushSubscriptions.FirstOrDefault(s => s.Endpoint == endpoint);
        if (sub is not null) _pushSubscriptions.Remove(sub);
    }
}
