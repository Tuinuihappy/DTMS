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
///
/// <para><b>Assign, never accumulate.</b> Every column here is overwritten
/// from the event, which is what makes a lost projection race harmless: the
/// loser would have written the same values anyway. api and outbox-worker
/// both consume these events, so a column updated as <c>x = x + 1</c> on a
/// loaded row can be read by two of them at once and lose a write with no
/// exception raised.</para>
///
/// <para><see cref="PauseCount"/> is the one accumulating column, and for
/// exactly that reason it has no setter method here — it is incremented in
/// SQL by <c>TripFactsProjectionStore.RecordPausedAsync</c>, under the row
/// lock Postgres holds for the statement. It used to do <c>+= 1</c> on a
/// loaded row (fixed 2026-09-11, after the same defect cost OrderFunnelHourly
/// real counts). Any future counter belongs in SQL the same way.</para>
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
    public DateTime? RejectedAt { get; private set; }
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

    // Late robot patch for a trip whose TripStarted carried no vehicle key
    // (recovered post-terminal via Trip.BackfillVendorVehicle). Fill-only-if-
    // empty + no lifecycle side effects — mirrors the SetStartedAt guard so a
    // real key already on the row is never clobbered.
    public void BackfillVendorVehicleKey(string vendorVehicleKey, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(vendorVehicleKey) || VendorVehicleKey is not null) return;
        VendorVehicleKey = vendorVehicleKey;
        UpdatedAt = at;
    }

    // Pausing is written by TripFactsProjectionStore.RecordPausedAsync in SQL,
    // not here: PauseCount accumulates, and `PauseCount += 1` on a loaded row
    // is a read-modify-write that two consumers can lose. There is deliberately
    // no RecordPaused() to call — see the class summary.

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

    public void SetRejectedAt(
        DateTime at, Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason)
    {
        RejectedAt = at;
        DeliveryOrderId ??= deliveryOrderId;
        JobId ??= jobId;
        if (!string.IsNullOrEmpty(vendorUpperKey)) VendorUpperKey = vendorUpperKey;
        FailureReason = reason;
        FinalStatus = "Rejected";
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
