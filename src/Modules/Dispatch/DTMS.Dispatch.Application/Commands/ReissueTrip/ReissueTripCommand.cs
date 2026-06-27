using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReissueTrip;

/// <summary>
/// Retry a terminal Trip (Cancelled only — Failed must first be reopened
/// at the DeliveryOrder level). Reuses the original Trip's route context
/// to invoke the envelope dispatch pipeline a second time, links the new
/// Trip to the original via PreviousAttemptId, and writes an immutable
/// TripRetryEvent audit record.
///
/// Returns the new Trip Id on success.
/// </summary>
public record ReissueTripCommand(
    Guid OriginalTripId,
    string RetrySource,        // "Manual" / "Automatic" / "Reopen"
    string? RetriedBy,
    string? RetryReason,
    Guid? CorrelationId = null
) : ICommand<Guid>;
