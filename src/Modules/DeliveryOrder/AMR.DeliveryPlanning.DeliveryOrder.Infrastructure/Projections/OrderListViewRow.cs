namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Phase P4 — Denormalized read-model row for the operator list / search
/// page. Materialized by <c>OrderListViewProjector</c> from Order + Trip
/// + Job integration events. Replaces the runtime <c>SearchAsync</c>
/// query in <see cref="Repositories.DeliveryOrderRepository"/> with an
/// index-backed lookup.
///
/// <para><b>Layout</b> — three groups of columns:</para>
/// <list type="bullet">
///   <item><b>Filter columns</b> — Status, Priority, TransportMode,
///         SourceSystem, HasFailedTrip, HasActiveJob. Each pair (or
///         common combo) gets a composite index for hot query paths.</item>
///   <item><b>Display columns</b> — RequestedBy, CreatedBy, totals,
///         POD flags, service-window timestamps. Read-only mirror of
///         the write side; populated at projection time so the list
///         page never joins.</item>
///   <item><b>Search column</b> — SearchText concatenates every field
///         users actually scan for; a GENERATED tsvector + GIN index
///         turns wildcard search from full-table-scan ILIKE into a
///         millisecond GIN probe.</item>
/// </list>
///
/// <para>The projector owns every write — handlers MUST NOT touch this
/// table. Deleting the table and replaying events (or running the
/// backfill SQL) reconstructs every row deterministically.</para>
/// </summary>
public class OrderListViewRow
{
    public Guid OrderId { get; private set; }
    public string OrderRef { get; private set; } = string.Empty;

    // ── Filter columns ─────────────────────────────────────────────────
    public string Status { get; private set; } = string.Empty;
    public string SourceSystem { get; private set; } = string.Empty;
    public string Priority { get; private set; } = string.Empty;
    public string? TransportMode { get; private set; }

    // Derived booleans recomputed every time a Trip/Job event lands.
    public bool HasFailedTrip { get; private set; }
    public bool HasActiveJob { get; private set; }
    public Guid? LatestTripId { get; private set; }
    public string? LatestJobStatus { get; private set; }

    // ── Display columns (denormalized from the write side) ────────────
    public string? RequestedBy { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? Notes { get; private set; }
    public int TotalItems { get; private set; }
    public double TotalQuantity { get; private set; }
    public double TotalWeightKg { get; private set; }
    public bool? RequiresDropPod { get; private set; }
    public bool? RequiresPickupPod { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ServiceWindowEarliestUtc { get; private set; }
    public DateTime? ServiceWindowLatestUtc { get; private set; }

    // ── Search column ──────────────────────────────────────────────────
    // Concatenated free-text scratch from which Postgres derives a
    // tsvector via a GENERATED column. The projector writes plain text;
    // the database (and EF migration below) owns the tsvector + GIN
    // index. EF only sees the text column.
    public string SearchText { get; private set; } = string.Empty;

    private OrderListViewRow() { }   // EF

    public OrderListViewRow(
        Guid orderId, string orderRef, string status, string sourceSystem, string priority,
        string? transportMode,
        string? requestedBy, string? createdBy, string? notes,
        int totalItems, double totalQuantity, double totalWeightKg,
        bool? requiresDropPod, bool? requiresPickupPod,
        DateTime createdAt, DateTime? updatedAt, DateTime? submittedAt,
        DateTime? serviceWindowEarliestUtc, DateTime? serviceWindowLatestUtc,
        string searchText)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(orderRef))
            throw new ArgumentException("OrderRef is required.", nameof(orderRef));

        OrderId = orderId;
        OrderRef = orderRef;
        Status = status;
        SourceSystem = sourceSystem;
        Priority = priority;
        TransportMode = transportMode;
        RequestedBy = requestedBy;
        CreatedBy = createdBy;
        Notes = notes;
        TotalItems = totalItems;
        TotalQuantity = totalQuantity;
        TotalWeightKg = totalWeightKg;
        RequiresDropPod = requiresDropPod;
        RequiresPickupPod = requiresPickupPod;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        SubmittedAt = submittedAt;
        ServiceWindowEarliestUtc = serviceWindowEarliestUtc;
        ServiceWindowLatestUtc = serviceWindowLatestUtc;
        SearchText = searchText;
    }

    // ── Mutation surface (projector only) ─────────────────────────────

    public void SetTripDerivedFields(bool hasFailedTrip, Guid? latestTripId)
    {
        HasFailedTrip = hasFailedTrip;
        LatestTripId = latestTripId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetJobDerivedFields(bool hasActiveJob, string? latestJobStatus)
    {
        HasActiveJob = hasActiveJob;
        LatestJobStatus = latestJobStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecomputeSearchText(string searchText)
        => SearchText = searchText;

    // Phase P4.6 — single mutation point for "refresh from aggregate".
    // Rewrites every display column (NOT trip/job derived flags — those
    // are owned by SetTripDerivedFields / SetJobDerivedFields and would
    // be lost if we recomputed them here from the order alone).
    public void RefreshFromAggregate(
        string status,
        string sourceSystem,
        string priority,
        string? transportMode,
        string? requestedBy,
        string? createdBy,
        string? notes,
        int totalItems,
        double totalQuantity,
        double totalWeightKg,
        bool? requiresDropPod,
        bool? requiresPickupPod,
        DateTime? submittedAt,
        DateTime? serviceWindowEarliestUtc,
        DateTime? serviceWindowLatestUtc,
        string searchText,
        DateTime occurredAt)
    {
        Status = status;
        SourceSystem = sourceSystem;
        Priority = priority;
        TransportMode = transportMode;
        RequestedBy = requestedBy;
        CreatedBy = createdBy;
        Notes = notes;
        TotalItems = totalItems;
        TotalQuantity = totalQuantity;
        TotalWeightKg = totalWeightKg;
        RequiresDropPod = requiresDropPod;
        RequiresPickupPod = requiresPickupPod;
        SubmittedAt = submittedAt;
        ServiceWindowEarliestUtc = serviceWindowEarliestUtc;
        ServiceWindowLatestUtc = serviceWindowLatestUtc;
        SearchText = searchText;
        UpdatedAt = occurredAt;
    }
}
