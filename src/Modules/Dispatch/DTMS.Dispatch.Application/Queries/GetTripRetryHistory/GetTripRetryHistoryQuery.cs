using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetTripRetryHistory;

/// <summary>
/// Returns every Trip in a retry chain for the given trip's group
/// (same Pickup/Drop pair on the same DeliveryOrder), sorted by
/// AttemptNumber ascending. Each entry is enriched with the
/// TripRetryEvent that produced it (null for attempt #1 — the
/// original dispatch wasn't a retry).
///
/// Operator UI uses this to render a vertical timeline of attempts
/// with the "who retried, why, when" context inline.
/// </summary>
public record GetTripRetryHistoryQuery(Guid TripId) : IQuery<TripRetryHistoryDto>;

public sealed record TripRetryHistoryDto(
    Guid TripId,
    int TotalAttempts,
    IReadOnlyList<TripChainEntryDto> Attempts);

public sealed record TripChainEntryDto(
    Guid TripId,
    int AttemptNumber,
    string Status,
    string UpperKey,
    string? VendorOrderKey,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureReason,
    bool IsCurrent,                   // matches the TripId in the query
    TripRetryTriggerDto? RetryTrigger); // the audit that brought us TO this attempt

public sealed record TripRetryTriggerDto(
    Guid Id,
    DateTime OccurredAt,
    string RetrySource,    // "Manual" / "Automatic" / "Reopen"
    string? RetriedBy,
    string? RetryReason,
    string OriginalStatus);
