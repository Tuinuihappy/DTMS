using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetFullOrderAudit;

/// <summary>
/// Consolidated audit log for one DeliveryOrder. Pulls from four sources
/// and sorts them into one chronologically-ordered stream so support and
/// compliance can answer "what happened, when, who did it, why" without
/// stitching together three separate views.
///
/// Sources merged:
///   • OrderAuditEvents      — order-level status changes (DeliveryOrder module)
///   • OrderAmendments        — service-window / metadata edits
///   • Trip ExecutionEvents   — dispatch / vendor lifecycle per trip
///   • TripRetryEvents        — every retry attempt's trigger
/// </summary>
public record GetFullOrderAuditQuery(Guid OrderId) : IQuery<FullOrderAuditDto>;

public sealed record FullOrderAuditDto(
    Guid OrderId,
    int TotalEntries,
    IReadOnlyList<FullAuditEntryDto> Entries);

public sealed record FullAuditEntryDto(
    Guid Id,
    string Source,             // "Order" | "Amendment" | "TripExecution" | "TripRetry"
    string EventType,
    string? Details,
    string? ActorId,
    DateTime OccurredAt,
    // Trip-level events carry these so the UI can link / group:
    Guid? RelatedTripId,
    int? AttemptNumber,
    // S.1 follow-up — Channel (ManualWeb / OperatorPwa / SystemApi /
    // InternalJob) and DisplayName lifted from ActorContext at write
    // time. Null for rows projected from pre-1.2 events or backfilled
    // from the four legacy sources; UI degrades gracefully.
    string? Channel = null,
    string? DisplayName = null,
    // Phase C (multi-source) — which external system an upstream-callback
    // row concerns ('oms', 'sap', …). Null for non-callback rows.
    string? SystemKey = null);
