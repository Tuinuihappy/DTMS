using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Events;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.SharedKernel.Domain;

namespace DTMS.Dispatch.Domain.Entities;

// Envelope-dispatched trip. RIOT3 is the source of truth for execution;
// DTMS just mirrors task lifecycle via the webhook → MarkVendor* path.
// (Legacy per-task scheduling was removed in Phase b7.)
public class Trip : AggregateRoot<Guid>
{
    // Phase b8 — Planning creates a 1:1 Job anchor per station-pair group
    // and passes its Id through. Pre-b8 envelope rows have Guid.Empty
    // (filtered out of the IX_Trips_JobId index).
    public Guid JobId { get; private set; }
    public Guid DeliveryOrderId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public TripStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    // UpperKey is the DTMS-side correlation key RIOT3 echoes back on every
    // webhook. Lives on Trip core because Manual / Fleet trips will get
    // analogous correlation keys later (per Phase 3 plan); the field is
    // mode-agnostic at the type level even though only AMR populates it today.
    public string UpperKey { get; private set; } = string.Empty;

    // Phase 3b — AMR-specific vendor fields (VendorOrderKey, VendorVehicleKey,
    // VendorVehicleName, VendorPauseSource) moved off Trip core into a
    // 1:0..1 navigation. AMR trips create the extension on demand; Manual /
    // Fleet trips never touch it. Mirror entities (ManualTripExtension,
    // FleetTripExtension) follow the same shape in Phase 4 / 5.
    public AmrTripExtension? AmrExtension { get; private set; }

    // Read-only delegations so existing in-memory consumers (handlers
    // mapping a loaded Trip entity to a DTO, projectors mapping events)
    // don't all have to switch to `trip.AmrExtension?.X` syntax. EF query
    // projections still need explicit `t.AmrExtension!.X` because EF can't
    // translate these expression-bodied properties; see TripFactsRepo /
    // TripQueueRepo for examples that use the navigation directly.
    public string? VendorOrderKey => AmrExtension?.VendorOrderKey;
    public string? VendorVehicleKey => AmrExtension?.VendorVehicleKey;
    public string? VendorVehicleName => AmrExtension?.VendorVehicleName;
    public VendorPauseSource? VendorPauseSource => AmrExtension?.VendorPauseSource;

    // Route context — the station pair the trip dispatches against.
    // Captured at create time so retry can re-resolve the OrderTemplate
    // without re-reading the DeliveryOrder items. Nullable because
    // trips persisted before retry support don't have these set.
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    // Phase 2.5 — snapshot the warehouse Ids at create time. Mirrors the
    // existing StationId snapshot pattern but at the building level (per
    // ADR-002): every trip is associated with a pickup + drop warehouse,
    // even Manual / Fleet that don't have specific stations. Nullable now
    // because the existing CreateForEnvelope callers don't supply them
    // yet — Phase 2.6 wires resolution into the create command handler.
    public Guid? PickupWarehouseId { get; private set; }
    public Guid? DropWarehouseId { get; private set; }

    // Retry chain. First dispatch = 1, each retry increments.
    // PreviousAttemptId points to the trip this one supersedes (null for
    // first attempt). See TripRetryEvent for the immutable audit record.
    public int AttemptNumber { get; private set; } = 1;
    public Guid? PreviousAttemptId { get; private set; }

    // ── Vendor detail snapshots ──────────────────────────────────────────
    // Lifted fields are extracted at dispatch time from the OrderTemplate
    // we resolved + the request we built. They're indexable for dashboards
    // and sort/filter queries. Raw blobs are the authoritative forensic
    // record — they survive vendor schema drift.
    //
    // TemplateNameAtDispatch / PriorityAtDispatch come from OUR request
    // (what DTMS sent). VendorExpectedCompletionAt comes from the vendor's
    // RESPONSE at terminal time.
    public string? TemplateNameAtDispatch { get; private set; }
    public int? PriorityAtDispatch { get; private set; }
    public DateTime? VendorExpectedCompletionAt { get; private set; }

    /// <summary>Frozen copy of the JSON DTMS POSTed to RIOT3 at dispatch.
    /// Used for compliance / "what exactly did we send" forensic queries.
    /// </summary>
    public string? VendorRequestSnapshot { get; private set; }

    /// <summary>Frozen copy of RIOT3's GET response captured once the trip
    /// reaches a terminal state (FINISHED/FAILED/CANCELED). Null until
    /// the capture consumer succeeds; idempotent (first write wins).</summary>
    public string? VendorFinalSnapshot { get; private set; }

    private readonly List<ExecutionEvent> _events = new();
    public IReadOnlyCollection<ExecutionEvent> Events => _events.AsReadOnly();

    private readonly List<TripException> _exceptions = new();
    public IReadOnlyCollection<TripException> Exceptions => _exceptions.AsReadOnly();

    private readonly List<ProofOfDelivery> _proofs = new();
    public IReadOnlyCollection<ProofOfDelivery> ProofsOfDelivery => _proofs.AsReadOnly();

    private Trip() { }

    public static Trip CreateForEnvelope(
        Guid deliveryOrderId,
        string upperKey,
        string? vendorOrderKey,
        Guid? pickupStationId = null,
        Guid? dropStationId = null,
        int attemptNumber = 1,
        Guid? previousAttemptId = null,
        string? templateNameAtDispatch = null,
        int? priorityAtDispatch = null,
        string? vendorRequestSnapshot = null,
        Guid? jobId = null,
        Guid? pickupWarehouseId = null,
        Guid? dropWarehouseId = null)
    {
        if (string.IsNullOrWhiteSpace(upperKey))
            throw new ArgumentException("UpperKey must not be empty.", nameof(upperKey));
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "AttemptNumber must be >= 1.");

        // VendorOrderKey may be missing if RIOT3 accepted the envelope but
        // didn't return an orderKey in the response body. Correlation still
        // works because the webhook keys off UpperKey (which we always send).
        var trimmedVendor = string.IsNullOrWhiteSpace(vendorOrderKey) ? null : vendorOrderKey.Trim();

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            JobId = jobId ?? Guid.Empty,
            DeliveryOrderId = deliveryOrderId,
            VehicleId = null,
            Status = TripStatus.Created,
            CreatedAt = DateTime.UtcNow,
            UpperKey = upperKey.Trim(),
            PickupStationId = pickupStationId,
            DropStationId = dropStationId,
            PickupWarehouseId = pickupWarehouseId,
            DropWarehouseId = dropWarehouseId,
            AttemptNumber = attemptNumber,
            PreviousAttemptId = previousAttemptId,
            TemplateNameAtDispatch = string.IsNullOrWhiteSpace(templateNameAtDispatch) ? null : templateNameAtDispatch.Trim(),
            PriorityAtDispatch = priorityAtDispatch,
            VendorRequestSnapshot = string.IsNullOrWhiteSpace(vendorRequestSnapshot) ? null : vendorRequestSnapshot
        };
        // Phase 3b — AMR vendor key lives on the extension entity now.
        // Created on demand so Manual / Fleet callers (which pass
        // vendorOrderKey=null) don't create an empty AmrTripExtension row.
        if (trimmedVendor is not null)
        {
            trip.AmrExtension = AmrTripExtension.Create(trip.Id);
            trip.AmrExtension.AttachVendorOrder(trimmedVendor);
        }
        var detail = $"vendorOrderKey={trimmedVendor ?? "(empty)"} attempt={attemptNumber}";
        if (previousAttemptId.HasValue) detail += $" retryOf={previousAttemptId.Value}";
        if (!string.IsNullOrWhiteSpace(templateNameAtDispatch)) detail += $" template={templateNameAtDispatch}";
        trip.RecordEvent("EnvelopeDispatched", detail);
        return trip;
    }

    /// <summary>
    /// Persist the vendor's authoritative final state. Captured by either
    /// the webhook-driven CaptureFinalSnapshotConsumer or the reconciler;
    /// both are guarded by an "if null" check so this is idempotent at the
    /// caller. The JSON blob is the source of truth — keep it raw to
    /// survive any vendor schema drift.
    /// </summary>
    public void CaptureFinalSnapshot(string snapshotJson, DateTime? expectedCompletionAt = null)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            throw new ArgumentException("Final snapshot JSON must not be empty.", nameof(snapshotJson));

        // First write wins. Callers should guard for null before calling,
        // but the domain enforces it too so race-driven duplicates are safe.
        if (VendorFinalSnapshot is not null) return;

        VendorFinalSnapshot = snapshotJson;
        if (expectedCompletionAt.HasValue && !VendorExpectedCompletionAt.HasValue)
            VendorExpectedCompletionAt = expectedCompletionAt.Value;

        RecordEvent("VendorSnapshotCaptured", $"sizeBytes={snapshotJson.Length}");
    }

    // Envelope-flow vendor state transitions. All idempotent — duplicate
    // webhooks (or race with the reconciliation poller) are safe.

    public void MarkVendorStarted(
        Guid? vehicleId = null,
        string? vendorVehicleKey = null,
        string? vendorVehicleName = null,
        IReadOnlyList<TripItemSnapshot>? items = null)
    {
        // Phase 3d — vehicle assignment is always recorded (audit + cache
        // update), even on repeat TASK_PROCESSING webhooks after the trip
        // already transitioned past Created. This is how RIOT3 reassignment
        // (robot A fails → robot B takes over) gets captured; the history
        // table grows, and the cache pointers on AmrExtension always reflect
        // the latest robot so PASS / CANCEL commands target the right one.
        // RecordVehicleAssignment is idempotent — same (key, name) as the
        // previous assignment is a no-op, so RIOT3's normal duplicate
        // webhooks don't bloat the history.
        if (!string.IsNullOrWhiteSpace(vendorVehicleKey) || !string.IsNullOrWhiteSpace(vendorVehicleName))
        {
            AmrExtension ??= AmrTripExtension.Create(Id);
            AmrExtension.RecordVehicleAssignment(
                vendorVehicleKey, vendorVehicleName,
                source: "TASK_PROCESSING",
                assignedAt: DateTime.UtcNow);
        }

        // Status transition + event fire — only on first MarkVendorStarted.
        // Subsequent webhooks (reassignment, retries) update vehicle
        // assignment above but don't re-emit TripStartedDomainEvent.
        if (Status != TripStatus.Created)
            return;
        if (vehicleId.HasValue && !VehicleId.HasValue)
            VehicleId = vehicleId.Value;
        Status = TripStatus.InProgress;
        StartedAt = DateTime.UtcNow;
        RecordEvent("VendorStarted", vendorVehicleKey ?? vehicleId?.ToString());
        // Fires TripStartedIntegrationEvent via the outbox so the
        // DeliveryOrder side can transition Dispatched → InProgress
        // (Option A — order-level visibility). Phase P5.3 — Items
        // snapshot is forwarded so TripItemsProjector can populate
        // dispatch.TripItems for the operator drawer.
        AddDomainEvent(new TripStartedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, vehicleId,
            AmrExtension?.VendorVehicleKey,
            Items: items));
    }

    /// <summary>
    /// Vendor reports the robot finished its pickup action at this trip's
    /// pickup station — items are now physically loaded and in transit.
    /// Doesn't change Trip.Status (the trip is still InProgress) — fires
    /// an integration event so DeliveryOrder can flip its items
    /// Pending → Picked. Idempotent: the consumer guards on item state,
    /// and the underlying webhook handler only calls this once the
    /// pickup station's mission finished.
    /// </summary>
    public void MarkVendorPickedUp()
    {
        // Pickup only makes sense while the trip is in flight. Fail-loud
        // would mask network races; bail quietly instead.
        if (Status is not TripStatus.InProgress) return;

        RecordEvent("VendorPickupCompleted", null);
        AddDomainEvent(new TripPickupCompletedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId));
    }

    /// <summary>
    /// Vendor reports the robot finished its drop action at this trip's
    /// drop station — items are physically at the dock. Doesn't change
    /// Trip.Status (still InProgress until TASK_FINISHED).
    ///
    /// <paramref name="requiresDropPod"/> is the parent order's POD policy,
    /// resolved by the caller (vendor webhook) so the integration event
    /// can carry it through to downstream consumers without a cross-module
    /// read at projection time. Null = unknown; consumers fall back to
    /// the legacy "land at DroppedOff" path. False = items land at
    /// Delivered immediately (no POD required); true = items hold at
    /// DroppedOff pending operator /pod-scan.
    /// </summary>
    public void MarkVendorDropCompleted(bool? requiresDropPod = null)
    {
        if (Status is not TripStatus.InProgress) return;
        RecordEvent("VendorDropCompleted", requiresDropPod is null ? null : $"requiresDropPod={requiresDropPod}");
        AddDomainEvent(new TripDropCompletedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, requiresDropPod));
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

    public void Pause(VendorPauseSource source)
    {
        if (Status != TripStatus.InProgress)
            throw new InvalidOperationException("Only InProgress trips can be paused.");

        Status = TripStatus.Paused;
        // Phase 3b — pause source lives on the AMR extension. Manual /
        // Fleet pauses (Phase 4 / 5) will populate their own extension's
        // equivalent column; the Trip-level Status flip is mode-agnostic.
        AmrExtension ??= AmrTripExtension.Create(Id);
        AmrExtension.SetPauseSource(source);
        AddDomainEvent(new TripPausedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        RecordEvent("TripPaused", source.ToString());
    }

    public void Resume()
    {
        if (Status != TripStatus.Paused)
            throw new InvalidOperationException("Only Paused trips can be resumed.");

        Status = TripStatus.InProgress;
        AmrExtension?.ClearPauseSource();
        AddDomainEvent(new TripResumedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        RecordEvent("TripResumed", null);
    }

    // Operator confirms a robot waiting at a checkpoint may proceed (RIOT3 PASS).
    // Trip.Status is intentionally unchanged — this is an interactive nudge at
    // robot level, not a state transition. Requires VendorVehicleKey because
    // RIOT3 routes PASS by deviceKey, not by orderKey. Phase 3b — key lives
    // on the AMR extension; AMR-only call is a no-op for Manual/Fleet trips.
    public void AcknowledgeRobotPass()
    {
        if (Status != TripStatus.InProgress)
            throw new InvalidOperationException("Only InProgress trips can acknowledge a robot pass.");
        var vehicleKey = AmrExtension?.VendorVehicleKey;
        if (string.IsNullOrWhiteSpace(vehicleKey))
            throw new InvalidOperationException("Cannot pass — no vendor vehicle key on file.");

        RecordEvent("RobotPassAcknowledged", vehicleKey);
        AddDomainEvent(new TripRobotPassAcknowledgedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, vehicleKey));
    }

    public void Cancel(string reason)
    {
        if (Status == TripStatus.Completed || Status == TripStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a trip in {Status} status.");

        Status = TripStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        AddDomainEvent(new TripCancelledDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, JobId, DeliveryOrderId, reason,
            string.IsNullOrWhiteSpace(UpperKey) ? null : UpperKey));
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
