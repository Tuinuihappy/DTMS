using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

// Envelope-dispatched trip. RIOT3 is the source of truth for execution;
// DTMS just mirrors task lifecycle via the webhook → MarkVendor* path.
// (Legacy per-task scheduling was removed in Phase b7.)
public class Trip : AggregateRoot<Guid>
{
    // JobId is kept as a column for backward compatibility with existing
    // rows. New envelope trips set Guid.Empty since no Planning Job is
    // created in that path.
    public Guid JobId { get; private set; }
    public Guid DeliveryOrderId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public TripStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    // Envelope dispatch correlation fields. UpperKey is the DTMS-side key
    // RIOT3 echoes back on every webhook; VendorOrderKey is what RIOT3
    // assigned.
    public string UpperKey { get; private set; } = string.Empty;
    public string? VendorOrderKey { get; private set; }

    private readonly List<ExecutionEvent> _events = new();
    public IReadOnlyCollection<ExecutionEvent> Events => _events.AsReadOnly();

    private readonly List<TripException> _exceptions = new();
    public IReadOnlyCollection<TripException> Exceptions => _exceptions.AsReadOnly();

    private readonly List<ProofOfDelivery> _proofs = new();
    public IReadOnlyCollection<ProofOfDelivery> ProofsOfDelivery => _proofs.AsReadOnly();

    private Trip() { }

    public static Trip CreateForEnvelope(Guid deliveryOrderId, string upperKey, string? vendorOrderKey)
    {
        if (string.IsNullOrWhiteSpace(upperKey))
            throw new ArgumentException("UpperKey must not be empty.", nameof(upperKey));

        // VendorOrderKey may be missing if RIOT3 accepted the envelope but
        // didn't return an orderKey in the response body. Correlation still
        // works because the webhook keys off UpperKey (which we always send).
        var trimmedVendor = string.IsNullOrWhiteSpace(vendorOrderKey) ? null : vendorOrderKey.Trim();

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            JobId = Guid.Empty,
            DeliveryOrderId = deliveryOrderId,
            VehicleId = null,
            Status = TripStatus.Created,
            CreatedAt = DateTime.UtcNow,
            UpperKey = upperKey.Trim(),
            VendorOrderKey = trimmedVendor
        };
        trip.RecordEvent("EnvelopeDispatched", $"vendorOrderKey={trimmedVendor ?? "(empty)"}");
        return trip;
    }

    // Envelope-flow vendor state transitions. All idempotent — duplicate
    // webhooks (or race with the reconciliation poller) are safe.

    public void MarkVendorStarted(Guid? vehicleId = null)
    {
        if (Status != TripStatus.Created)
            return;
        if (vehicleId.HasValue && !VehicleId.HasValue)
            VehicleId = vehicleId.Value;
        Status = TripStatus.InProgress;
        StartedAt = DateTime.UtcNow;
        RecordEvent("VendorStarted", vehicleId?.ToString());
    }

    public void MarkVendorCompleted()
    {
        if (Status == TripStatus.Completed)
            return;
        if (Status == TripStatus.Cancelled || Status == TripStatus.Failed)
            throw new InvalidOperationException($"Cannot complete a trip in {Status} status.");

        Status = TripStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        RecordEvent("VendorCompleted", null);
        AddDomainEvent(new TripCompletedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, JobId, DeliveryOrderId, UpperKey));
    }

    public void MarkVendorFailed(string reason)
    {
        if (Status == TripStatus.Failed)
            return;
        if (Status == TripStatus.Completed)
            throw new InvalidOperationException("Cannot fail a completed trip.");

        Status = TripStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;
        RecordEvent("VendorFailed", reason);
        AddDomainEvent(new TripFailedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, JobId, DeliveryOrderId, reason, UpperKey));
    }

    public void Pause()
    {
        if (Status != TripStatus.InProgress)
            throw new InvalidOperationException("Only InProgress trips can be paused.");

        Status = TripStatus.Paused;
        AddDomainEvent(new TripPausedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        RecordEvent("TripPaused", null);
    }

    public void Resume()
    {
        if (Status != TripStatus.Paused)
            throw new InvalidOperationException("Only Paused trips can be resumed.");

        Status = TripStatus.InProgress;
        AddDomainEvent(new TripResumedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        RecordEvent("TripResumed", null);
    }

    public void Cancel(string reason)
    {
        if (Status == TripStatus.Completed || Status == TripStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a trip in {Status} status.");

        Status = TripStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        AddDomainEvent(new TripCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, JobId, reason));
        RecordEvent("TripCancelled", reason);
    }

    // Bind the trip to a vehicle the vendor (RIOT3) auto-selected after
    // dispatch. Idempotent: a no-op if the same vehicle is reported again,
    // throws if a different vehicle tries to take over.
    public void SetAssignedVehicle(Guid vehicleId)
    {
        if (vehicleId == Guid.Empty)
            throw new ArgumentException("VehicleId must not be empty.", nameof(vehicleId));

        if (Status == TripStatus.Completed || Status == TripStatus.Cancelled)
            throw new InvalidOperationException($"Cannot assign a vehicle to a trip in {Status} status.");

        if (VehicleId == vehicleId)
            return;

        if (VehicleId.HasValue)
            throw new InvalidOperationException(
                $"Trip already has VehicleId {VehicleId}; a different vehicle ({vehicleId}) cannot take over.");

        VehicleId = vehicleId;
        AddDomainEvent(new TripVehicleAssignedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, vehicleId));
        RecordEvent("TripVehicleAssigned", vehicleId.ToString());
    }

    public TripException RaiseException(string code, string severity, string detail)
    {
        var exception = new TripException(Id, code, severity, detail);
        _exceptions.Add(exception);
        AddDomainEvent(new ExceptionRaisedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, JobId, exception.Id, code, severity, detail));
        RecordEvent("ExceptionRaised", $"[{severity}] {code}: {detail}");
        return exception;
    }

    public void ResolveException(Guid exceptionId, string resolution, string resolvedBy)
    {
        var exception = _exceptions.FirstOrDefault(e => e.Id == exceptionId)
            ?? throw new InvalidOperationException($"Exception {exceptionId} not found.");

        exception.Resolve(resolution, resolvedBy);
        AddDomainEvent(new ExceptionResolvedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, exceptionId, resolution));
        RecordEvent("ExceptionResolved", $"{exceptionId}: {resolution}");
    }

    public ProofOfDelivery CaptureProofOfDelivery(Guid stopId, string? photoUrl, string? signatureData, List<string>? scannedIds, string? notes)
    {
        var pod = new ProofOfDelivery(Id, stopId, photoUrl, signatureData, scannedIds, notes);
        _proofs.Add(pod);
        AddDomainEvent(new PodCapturedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, stopId,
            (scannedIds ?? []).AsReadOnly()));
        RecordEvent("PodCaptured", $"Stop {stopId}, {scannedIds?.Count ?? 0} item(s) scanned");
        return pod;
    }

    private void RecordEvent(string eventType, string? details)
    {
        _events.Add(new ExecutionEvent(Id, null, eventType, details));
    }
}
