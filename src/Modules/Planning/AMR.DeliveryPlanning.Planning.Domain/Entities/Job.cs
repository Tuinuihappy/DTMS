using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

public class Job : AggregateRoot<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public JobStatus Status { get; private set; }
    public Guid? AssignedVehicleId { get; private set; }
    public string Priority { get; private set; } = "Normal";
    public double EstimatedDuration { get; private set; }
    public double EstimatedDistance { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Phase 2: Pattern + Multi-order support
    public PatternType Pattern { get; private set; } = PatternType.PointToPoint;
    public string? RequiredCapability { get; private set; }
    public double TotalWeight { get; private set; }
    // Transport mode the planner uses for this job — sourced from
    // DeliveryOrder.RequestedTransportMode at creation; null = unspecified
    // (planner chose whatever default the dispatch layer applies).
    public string? TransportMode { get; private set; }

    // Phase 4: Package assignment
    private readonly List<string> _packageBarcodes = new();
    public IReadOnlyCollection<string> PackageBarcodes => _packageBarcodes.AsReadOnly();

    // Phase 4+: Explainability + Predictive replanning
    public string? PlanningTrace { get; private set; }
    public DateTime? SlaDeadline { get; private set; }

    // Phase b8: envelope-dispatch anchor fields. Job is created 1:1 with a
    // station-pair group before the order is marked Planned, then either
    // MarkDispatched(tripId) on vendor accept or MarkFailed(reason) on
    // template lookup / vendor reject / orphan trip persistence. AttemptNumber
    // increments each time Retry() is called.
    public Guid? TripId { get; private set; }
    public string? VendorOrderKey { get; private set; }
    public string? FailureReason { get; private set; }
    // Phase b13 — Structured classification of the failure. Co-exists with
    // the free-text FailureReason: category answers "which bucket" without
    // pattern-matching on the upstream error string. Defaults to None for
    // pre-b13 rows (the migration sets every existing row to None too).
    public JobFailureCategory FailureCategory { get; private set; } = JobFailureCategory.None;
    public int AttemptNumber { get; private set; } = 1;
    // 1-based group index within the original order's station-pair grouping.
    // Required so Retry() can rebuild the EnvelopeUpperKey with a bumped
    // attempt suffix without re-reading the original DeliveryOrder.
    // Defaults to 1 for jobs created by the manual CreateJobFromOrder path
    // (which is single-group by nature).
    public int GroupIndex { get; private set; } = 1;
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    private readonly List<Guid> _derivedFromOrders = new();
    public IReadOnlyCollection<Guid> DerivedFromOrders => _derivedFromOrders.AsReadOnly();

    private readonly List<Leg> _legs = new();
    public IReadOnlyCollection<Leg> Legs => _legs.AsReadOnly();

    private Job() { } // EF Core

    public Job(Guid deliveryOrderId, string priority)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        Priority = priority;
        Status = JobStatus.Created;
        CreatedAt = DateTime.UtcNow;
        _derivedFromOrders.Add(deliveryOrderId);

        AddDomainEvent(new JobCreatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId));
    }

    /// <summary>
    /// Create a Job from multiple consolidated orders.
    /// </summary>
    public Job(List<Guid> orderIds, string priority, PatternType pattern)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = orderIds.First();
        Priority = priority;
        Pattern = pattern;
        Status = JobStatus.Created;
        CreatedAt = DateTime.UtcNow;
        _derivedFromOrders.AddRange(orderIds);

        AddDomainEvent(new JobCreatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId));
    }

    public void SetPattern(PatternType pattern) => Pattern = pattern;
    public void SetRequiredCapability(string capability) => RequiredCapability = capability;
    public void SetTotalWeight(double weight) => TotalWeight = weight;
    public void SetSlaDeadline(DateTime deadline) => SlaDeadline = deadline;
    public void SetPlanningTrace(string trace) => PlanningTrace = trace;
    public void SetTransportMode(string mode) => TransportMode = mode;

    // Phase b8 — Set at creation time by CreateJobAnchorCommand so Retry()
    // can rebuild the EnvelopeUpperKey. Once set, immutable.
    public void SetEnvelopeAnchor(int groupIndex, Guid pickupStationId, Guid dropStationId)
    {
        if (groupIndex < 1) throw new ArgumentOutOfRangeException(nameof(groupIndex), "GroupIndex must be >= 1.");
        GroupIndex = groupIndex;
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }

    public void SetPackageBarcodes(IEnumerable<string> barcodes)
    {
        _packageBarcodes.Clear();
        _packageBarcodes.AddRange(barcodes);
    }

    public bool IsSlaAtRisk(TimeSpan estimatedRemainingTime)
        => SlaDeadline.HasValue && SlaDeadline.Value - DateTime.UtcNow <= estimatedRemainingTime;

    public Leg AddLeg(Guid fromStationId, Guid toStationId, int sequenceOrder, double estimatedCost)
    {
        var leg = new Leg(Id, fromStationId, toStationId, sequenceOrder, estimatedCost);
        _legs.Add(leg);
        EstimatedDistance += estimatedCost;
        return leg;
    }

    public void AssignVehicle(Guid vehicleId, double estimatedDuration)
    {
        if (Status != JobStatus.Created)
            throw new InvalidOperationException("Only jobs in Created status can be assigned.");

        AssignedVehicleId = vehicleId;
        EstimatedDuration = estimatedDuration;
        Status = JobStatus.Assigned;

        AddDomainEvent(new JobAssignedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, vehicleId));
    }

    public void Commit()
    {
        if (Status != JobStatus.Assigned && Status != JobStatus.Created)
            throw new InvalidOperationException("Only Created or Assigned jobs can be committed.");

        Status = JobStatus.Committed;
        var legs = _legs
            .OrderBy(l => l.SequenceOrder)
            .Select(l => new CommittedLegSnapshot(l.FromStationId, l.ToStationId, l.SequenceOrder))
            .ToList();

        AddDomainEvent(new JobCommittedDomainEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Id,
            DeliveryOrderId,
            AssignedVehicleId,
            legs));
    }

    /// <summary>
    /// Replan: reset a committed job so it can be re-assigned to a different vehicle.
    /// </summary>
    public void Replan(string reason)
    {
        if (Status != JobStatus.Committed && Status != JobStatus.Assigned)
            throw new InvalidOperationException($"Cannot replan a job in {Status} status.");

        AssignedVehicleId = null;
        EstimatedDuration = 0;
        Status = JobStatus.Created;
    }

    /// <summary>
    /// Envelope dispatch succeeded: vendor accepted the order and a Trip
    /// row has been persisted. tripId may be Guid.Empty for the orphan
    /// case (vendor accepted but Trip persistence failed) — callers
    /// should treat that as MarkFailed instead.
    /// </summary>
    public void MarkDispatched(Guid tripId, string? vendorOrderKey)
    {
        if (Status != JobStatus.Created)
            throw new InvalidOperationException($"Cannot mark job {Id} dispatched from {Status}.");

        Status = JobStatus.Dispatched;
        TripId = tripId;
        VendorOrderKey = vendorOrderKey;
        FailureReason = null;

        AddDomainEvent(new JobDispatchedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, tripId, vendorOrderKey, AttemptNumber));
    }

    /// <summary>
    /// Envelope dispatch or vendor execution failed. Two sources:
    /// (1) dispatch-time — Created → Failed (template missing / vendor 4xx
    ///     / orphan trip persistence) — set by the consumer at hand-off.
    /// (2) vendor-time (Phase b9) — Dispatched/Executing → Failed via
    ///     TripFailedIntegrationEvent.
    /// Idempotent: a duplicate webhook for an already-Failed job is a no-op.
    /// <para>Phase b13 — Callers MUST classify the failure via
    /// <see cref="JobFailureCategory"/> so downstream queries can
    /// aggregate by category without text-pattern matching. The free-text
    /// reason still travels alongside for human display.</para>
    /// </summary>
    public void MarkFailed(string reason, JobFailureCategory category)
    {
        if (Status == JobStatus.Failed) return;  // webhook redelivery
        if (Status is not (JobStatus.Created or JobStatus.Dispatched or JobStatus.Executing))
            throw new InvalidOperationException($"Cannot mark job {Id} failed from {Status}.");

        Status = JobStatus.Failed;
        FailureReason = reason;
        FailureCategory = category;

        AddDomainEvent(new JobFailedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, reason, AttemptNumber));
    }

    /// <summary>
    /// Phase b9 — Vendor (RIOT3) reported the trip started executing.
    /// Idempotent. Only valid from Dispatched; later events for the same
    /// Trip (Pickup/Drop) don't bring us back here.
    /// </summary>
    public void MarkExecuting(Guid tripId)
    {
        if (Status == JobStatus.Executing) return;  // webhook redelivery
        // If the trip skipped straight to Completed/Failed/Cancelled before
        // we processed Started (out-of-order delivery), don't regress.
        if (Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled) return;
        if (Status != JobStatus.Dispatched)
            throw new InvalidOperationException($"Cannot mark job {Id} executing from {Status}.");

        Status = JobStatus.Executing;
        AddDomainEvent(new JobExecutingDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, tripId));
    }

    /// <summary>
    /// Phase b9 — Vendor reported the trip finished successfully.
    /// Idempotent. Accepts Dispatched (skip Executing if webhook missed) and
    /// Executing as valid origins.
    /// </summary>
    public void MarkCompleted(Guid tripId)
    {
        if (Status == JobStatus.Completed) return;  // webhook redelivery
        if (Status is not (JobStatus.Dispatched or JobStatus.Executing))
            throw new InvalidOperationException($"Cannot mark job {Id} completed from {Status}.");

        Status = JobStatus.Completed;
        AddDomainEvent(new JobCompletedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, tripId));
    }

    /// <summary>
    /// Phase b9 — Vendor or operator cancelled the trip mid-flight (e.g.
    /// RIOT3 abort with E700001). Terminal; Job.Retry() does not work from
    /// here — cancellation is intentional. Idempotent.
    /// </summary>
    public void MarkCancelled(Guid tripId, string reason)
    {
        if (Status == JobStatus.Cancelled) return;  // webhook redelivery
        // Completed is terminal-success — don't let a late cancellation
        // webhook flip it negative. Failed similarly stays.
        if (Status is JobStatus.Completed or JobStatus.Failed) return;
        if (Status is not (JobStatus.Dispatched or JobStatus.Executing))
            throw new InvalidOperationException($"Cannot mark job {Id} cancelled from {Status}.");

        Status = JobStatus.Cancelled;
        FailureReason = reason;
        // All MarkCancelled paths today are vendor/operator initiated via
        // TripCancelled webhook — category is fixed. If a future
        // dispatch-time cancellation pathway lands, add an overload.
        FailureCategory = JobFailureCategory.OperatorCancelled;
        AddDomainEvent(new JobCancelledDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, tripId, reason));
    }

    /// <summary>
    /// Reset a Failed job back to Created so it can be re-dispatched.
    /// AttemptNumber++, FailureReason cleared. TripId / VendorOrderKey
    /// are preserved as the *previous* attempt's lineage so the new
    /// dispatch can pass them as previousAttemptId / etc.
    /// </summary>
    public (Guid? PreviousTripId, int NewAttemptNumber) Retry()
    {
        if (Status != JobStatus.Failed)
            throw new InvalidOperationException($"Cannot retry a job in {Status} status.");

        var previousTripId = TripId;
        AttemptNumber++;
        Status = JobStatus.Created;
        FailureReason = null;
        TripId = null;
        VendorOrderKey = null;
        return (previousTripId, AttemptNumber);
    }

    /// <summary>
    /// Phase b9 — Externally initiated Trip-level retry produced a new
    /// Trip linked to this Job. Unlike Retry() (operator on the Job
    /// endpoint, strict Failed-only), this accepts Failed OR Cancelled
    /// because the Dispatch-side ReissueTrip command already validated
    /// the operator's intent. Bumps AttemptNumber and binds the new Trip.
    /// </summary>
    public void RebindToRetryTrip(Guid newTripId, string? newVendorOrderKey)
    {
        if (Status is not (JobStatus.Failed or JobStatus.Cancelled))
            throw new InvalidOperationException(
                $"Cannot rebind job {Id} from {Status} — only Failed or Cancelled jobs may receive a new Trip via Trip-level retry.");

        AttemptNumber++;
        Status = JobStatus.Dispatched;
        TripId = newTripId;
        VendorOrderKey = newVendorOrderKey;
        FailureReason = null;

        AddDomainEvent(new JobDispatchedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId, newTripId, newVendorOrderKey, AttemptNumber));
    }
}
