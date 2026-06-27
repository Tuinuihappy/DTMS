namespace DTMS.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Phase P5 — Reporting/BI fact table. One row per DeliveryOrder, with
/// every lifecycle transition flattened into a dedicated timestamp
/// column. Lives in the <c>bi</c> schema (cross-cutting BI prefix) but
/// owned + written exclusively by <c>OrderFactsProjector</c> inside the
/// DeliveryOrder module — keeps module boundary intact while making BI
/// queries trivial.
///
/// <para><b>Shape contrast with P1 OrderStatusHistory:</b> StatusHistory
/// is row-per-transition (long, narrow — feeds the detail-drawer
/// timeline). OrderFacts is row-per-order (short, wide — feeds
/// pre-built reports that ask "how long did orders take last week"
/// without any UNION/JOIN). The two are complementary: one is the
/// audit trail, the other is the analytics surface.</para>
///
/// <para><b>Derived columns</b> (TimeTo... and SlaBreached) are
/// PostgreSQL <c>GENERATED ALWAYS AS ... STORED</c> in the migration —
/// EF Core only sees them as read-only properties. That way the math
/// stays a single source of truth in the schema, never drifts from
/// what the projector wrote, and is indexable.</para>
/// </summary>
public class OrderFactsRow
{
    public Guid OrderId { get; private set; }

    // ── Dimensions (GROUP BY / WHERE) ──────────────────────────────────
    public string OrderRef { get; private set; } = string.Empty;
    public string SourceSystem { get; private set; } = string.Empty;
    public string Priority { get; private set; } = string.Empty;
    public string? TransportMode { get; private set; }
    public string? RequestedBy { get; private set; }
    public string FinalStatus { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }

    // ── Measures (SUM / AVG) ───────────────────────────────────────────
    public int TotalItems { get; private set; }
    public double TotalQuantity { get; private set; }
    public double TotalWeightKg { get; private set; }

    // ── Lifecycle timestamps (one column per status) ───────────────────
    public DateTime CreatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? DispatchedAt { get; private set; }
    public DateTime? InProgressAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? PartiallyCompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public DateTime? HeldAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // ── GENERATED STORED columns (read-only, computed by Postgres) ─────
    // Defined in the migration via raw SQL. EF sees them as read-only.
    public int? TimeToConfirmSec { get; private set; }
    public int? TimeToDispatchSec { get; private set; }
    public int? TimeToCompleteSec { get; private set; }
    public bool? SlaConfirmBreached { get; private set; }  // > 4h
    public bool? SlaCompleteBreached { get; private set; } // > 24h

    // ─── Factory: row is materialized lazily, first event creates it ───
    public static OrderFactsRow Create(
        Guid orderId,
        DateTime createdAt,
        string orderRef,
        string sourceSystem,
        string priority,
        string? transportMode,
        string? requestedBy,
        int totalItems,
        double totalQuantity,
        double totalWeightKg,
        string finalStatus)
        => new()
        {
            OrderId = orderId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            OrderRef = orderRef,
            SourceSystem = sourceSystem,
            Priority = priority,
            TransportMode = transportMode,
            RequestedBy = requestedBy,
            TotalItems = totalItems,
            TotalQuantity = totalQuantity,
            TotalWeightKg = totalWeightKg,
            FinalStatus = finalStatus,
        };

    // ─── Mutators (one per timestamp column) ──────────────────────────
    public void SetSubmittedAt(DateTime at) { SubmittedAt = at; UpdatedAt = at; }

    public void SetConfirmedAt(
        DateTime at,
        string priority,
        string? transportMode,
        int totalItems,
        double totalWeightKg)
    {
        ConfirmedAt = at;
        // The Confirmed event carries fresh dimensional data — refresh
        // measures the projector previously only knew via backfill.
        Priority = priority;
        TransportMode = transportMode;
        TotalItems = totalItems;
        TotalWeightKg = totalWeightKg;
        FinalStatus = "Confirmed";
        UpdatedAt = at;
    }

    public void SetDispatchedAt(DateTime at)
    { DispatchedAt = at; FinalStatus = "Dispatched"; UpdatedAt = at; }

    public void SetInProgressAt(DateTime at)
    { InProgressAt = at; FinalStatus = "InProgress"; UpdatedAt = at; }

    public void SetCompletedAt(DateTime at)
    { CompletedAt = at; FinalStatus = "Completed"; UpdatedAt = at; }

    public void SetPartiallyCompletedAt(DateTime at)
    { PartiallyCompletedAt = at; FinalStatus = "PartiallyCompleted"; UpdatedAt = at; }

    public void SetFailedAt(DateTime at, string? reason)
    { FailedAt = at; FailureReason = reason; FinalStatus = "Failed"; UpdatedAt = at; }

    public void SetCancelledAt(DateTime at, string? reason)
    { CancelledAt = at; FailureReason = reason; FinalStatus = "Cancelled"; UpdatedAt = at; }

    public void SetRejectedAt(DateTime at, string? reason)
    { RejectedAt = at; FailureReason = reason; FinalStatus = "Rejected"; UpdatedAt = at; }

    public void SetHeldAt(DateTime at, string? reason)
    { HeldAt = at; FailureReason = reason; FinalStatus = "Held"; UpdatedAt = at; }

    public void SetReleasedAt(DateTime at)
    { ReleasedAt = at; FailureReason = null; FinalStatus = "Confirmed"; UpdatedAt = at; }
}
