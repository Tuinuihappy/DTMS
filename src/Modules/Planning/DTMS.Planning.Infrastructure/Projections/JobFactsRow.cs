namespace DTMS.Planning.Infrastructure.Projections;

/// <summary>
/// Phase P5.2 — BI fact table for Job lifecycle. One row per Job,
/// each lifecycle status flattened into a timestamp column. Lives in
/// the cross-cutting <c>bi</c> schema, owned by the Planning module.
///
/// <para><b>Drives:</b> Job retry/queue health report — count + age
/// distribution by FailureReason, AttemptNumber, FinalStatus.</para>
/// </summary>
public class JobFactsRow
{
    public Guid JobId { get; private set; }

    // ── Dimensions ─────────────────────────────────────────────────────
    public Guid DeliveryOrderId { get; private set; }
    public Guid? AssignedVehicleId { get; private set; }
    public Guid? LatestTripId { get; private set; }
    public string? VendorOrderKey { get; private set; }
    public string FinalStatus { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }
    // Phase #9 — structured failure classification (b13). String, not
    // enum, because EF maps it to a varchar; matches the cross-module
    // wire format on the integration event. Defaults to "None".
    public string FailureCategory { get; private set; } = "None";
    public int AttemptNumber { get; private set; }

    // ── Lifecycle timestamps ───────────────────────────────────────────
    public DateTime CreatedAt { get; private set; }
    public DateTime? AssignedAt { get; private set; }
    public DateTime? CommittedAt { get; private set; }
    public DateTime? DispatchedAt { get; private set; }
    public DateTime? ExecutingAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // ── GENERATED STORED ───────────────────────────────────────────────
    public int? TimeToDispatchSec { get; private set; }
    public int? TimeToCompleteSec { get; private set; }
    public bool? SlaDispatchBreached { get; private set; }

    public static JobFactsRow Create(
        Guid jobId, Guid deliveryOrderId, DateTime createdAt, string finalStatus)
        => new()
        {
            JobId = jobId,
            DeliveryOrderId = deliveryOrderId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            FinalStatus = finalStatus,
        };

    public void SetAssignedAt(DateTime at, Guid? vehicleId)
    {
        AssignedAt = at;
        if (vehicleId.HasValue && vehicleId.Value != Guid.Empty)
            AssignedVehicleId = vehicleId;
        FinalStatus = "Assigned";
        UpdatedAt = at;
    }

    public void SetCommittedAt(DateTime at, Guid? vehicleId)
    {
        CommittedAt = at;
        if (vehicleId.HasValue && vehicleId.Value != Guid.Empty)
            AssignedVehicleId = vehicleId;
        FinalStatus = "Committed";
        UpdatedAt = at;
    }

    public void SetDispatchedAt(
        DateTime at, Guid? tripId, string? vendorOrderKey, int attemptNumber)
    {
        DispatchedAt = at;
        if (tripId.HasValue && tripId.Value != Guid.Empty) LatestTripId = tripId;
        if (!string.IsNullOrEmpty(vendorOrderKey)) VendorOrderKey = vendorOrderKey;
        AttemptNumber = attemptNumber;
        FinalStatus = "Dispatched";
        UpdatedAt = at;
    }

    public void SetExecutingAt(DateTime at, Guid? tripId)
    {
        ExecutingAt = at;
        if (tripId.HasValue && tripId.Value != Guid.Empty) LatestTripId = tripId;
        FinalStatus = "Executing";
        UpdatedAt = at;
    }

    public void SetCompletedAt(DateTime at, Guid? tripId)
    {
        CompletedAt = at;
        if (tripId.HasValue && tripId.Value != Guid.Empty) LatestTripId = tripId;
        FinalStatus = "Completed";
        UpdatedAt = at;
    }

    public void SetFailedAt(DateTime at, string? reason, int attemptNumber, string? category)
    {
        FailedAt = at;
        FailureReason = reason;
        // null/empty from upstream collapses to "None" so the column never
        // holds NULL — keeps GROUP BY queries from needing a COALESCE.
        FailureCategory = string.IsNullOrWhiteSpace(category) ? "None" : category;
        AttemptNumber = attemptNumber;
        FinalStatus = "Failed";
        UpdatedAt = at;
    }

    public void SetCancelledAt(DateTime at, Guid? tripId, string? reason, string? category)
    {
        CancelledAt = at;
        if (tripId.HasValue && tripId.Value != Guid.Empty) LatestTripId = tripId;
        FailureReason = reason;
        FailureCategory = string.IsNullOrWhiteSpace(category) ? "OperatorCancelled" : category;
        FinalStatus = "Cancelled";
        UpdatedAt = at;
    }
}
