namespace DTMS.Dispatch.Infrastructure.Projections;

/// <summary>
/// Phase P5.2 — BI fact table for Trip lifecycle. One row per Trip,
/// each lifecycle status flattened into a timestamp column. Lives in
/// the cross-cutting <c>bi</c> schema but owned by the Dispatch
/// module. Backfill SQL seeds rows for trips that existed before P5.2
/// shipped (no integration event marks Trip creation).
///
/// <para><b>Vendor performance reports</b> live on this table — the
/// <c>VendorUpperKey</c> dimension lets the analyst slice
/// AvgTimeToComplete by vendor, then export the slice as CSV.</para>
/// </summary>
public class TripFactsRow
{
    public Guid TripId { get; private set; }

    // ── Dimensions ─────────────────────────────────────────────────────
    public Guid? DeliveryOrderId { get; private set; }
    public Guid? JobId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public string? VendorUpperKey { get; private set; }
    // Phase #10 — Vehicle performance report groups by this dimension.
    // Captured from TripStartedIntegrationEvent V1.1 onward (deviceKey
    // RIOT3 echoes back). Backfill SQL seeds it from dispatch.Trips for
    // historical rows.
    public string? VendorVehicleKey { get; private set; }
    public string FinalStatus { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }
    public int PauseCount { get; private set; }

    // ── Lifecycle timestamps ───────────────────────────────────────────
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FirstPausedAt { get; private set; }
    public DateTime? LastResumedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // ── GENERATED STORED (computed by Postgres) ────────────────────────
    public int? TimeToStartSec { get; private set; }
    public int? TimeToCompleteSec { get; private set; }
    public bool? SlaCompleteBreached { get; private set; }

    public static TripFactsRow Create(
        Guid tripId,
        DateTime createdAt,
        Guid? deliveryOrderId,
        Guid? jobId,
        string finalStatus)
        => new()
        {
            TripId = tripId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            DeliveryOrderId = deliveryOrderId,
            JobId = jobId,
            FinalStatus = finalStatus,
        };

    public void SetStartedAt(
        DateTime at, Guid? deliveryOrderId, Guid? jobId, Guid? vehicleId,
        string? vendorVehicleKey)
    {
        StartedAt = at;
        DeliveryOrderId ??= deliveryOrderId;
        JobId ??= jobId;
        VehicleId ??= vehicleId;
        // First-write-wins — matches Trip aggregate's MarkVendorStarted
        // behaviour (empty/whitespace from upstream doesn't overwrite).
        if (!string.IsNullOrWhiteSpace(vendorVehicleKey) && VendorVehicleKey is null)
            VendorVehicleKey = vendorVehicleKey;
        FinalStatus = "InProgress";
        UpdatedAt = at;
    }

    public void RecordPaused(DateTime at)
    {
        FirstPausedAt ??= at;
        PauseCount += 1;
        FinalStatus = "Paused";
        UpdatedAt = at;
    }

    public void RecordResumed(DateTime at)
    {
        LastResumedAt = at;
        FinalStatus = "InProgress";
        UpdatedAt = at;
    }

    public void SetCompletedAt(
        DateTime at, Guid? deliveryOrderId, Guid? jobId, string? vendorUpperKey)
    {
        CompletedAt = at;
        DeliveryOrderId ??= deliveryOrderId;
        JobId ??= jobId;
        if (!string.IsNullOrEmpty(vendorUpperKey)) VendorUpperKey = vendorUpperKey;
        FinalStatus = "Completed";
        UpdatedAt = at;
    }

    public void SetFailedAt(
        DateTime at, Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason)
    {
        FailedAt = at;
        DeliveryOrderId ??= deliveryOrderId;
        JobId ??= jobId;
        if (!string.IsNullOrEmpty(vendorUpperKey)) VendorUpperKey = vendorUpperKey;
        FailureReason = reason;
        FinalStatus = "Failed";
        UpdatedAt = at;
    }

    public void SetCancelledAt(
        DateTime at, Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason)
    {
        CancelledAt = at;
        DeliveryOrderId ??= deliveryOrderId;
        JobId ??= jobId;
        if (!string.IsNullOrEmpty(vendorUpperKey)) VendorUpperKey = vendorUpperKey;
        FailureReason = reason;
        FinalStatus = "Cancelled";
        UpdatedAt = at;
    }
}
