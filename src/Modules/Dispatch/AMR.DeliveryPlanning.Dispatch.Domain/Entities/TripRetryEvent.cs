using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

/// <summary>
/// Immutable audit record of a Trip retry. One row written each time
/// an operator (or the system) reissues a cancelled / reopened-failed
/// trip. Persisted separately from <see cref="Trip"/> so the audit
/// trail has its own retention policy and stays append-only even when
/// the Trip aggregate is updated.
/// </summary>
public sealed class TripRetryEvent : Entity<Guid>
{
    public DateTime OccurredAt { get; private set; }
    public Guid OriginalTripId { get; private set; }
    public Guid NewTripId { get; private set; }
    public Guid DeliveryOrderId { get; private set; }
    public int AttemptNumber { get; private set; }

    /// <summary>"Manual" — operator-initiated; "Automatic" — system retry;
    /// "Reopen" — first retry after an Order reopen.</summary>
    public string RetrySource { get; private set; } = string.Empty;

    /// <summary>Operator user id, or "system" for automatic retries.</summary>
    public string? RetriedBy { get; private set; }
    public string? RetryReason { get; private set; }

    /// <summary>"Cancelled" or "Failed" — the terminal status of the
    /// original trip when the retry was requested.</summary>
    public string OriginalStatus { get; private set; } = string.Empty;

    /// <summary>Optional request-tracing id so multi-step retries can be
    /// correlated end-to-end across services.</summary>
    public Guid? CorrelationId { get; private set; }

    private TripRetryEvent() { }

    public static TripRetryEvent Record(
        Guid originalTripId,
        Guid newTripId,
        Guid deliveryOrderId,
        int attemptNumber,
        string originalStatus,
        string retrySource,
        string? retriedBy,
        string? retryReason,
        Guid? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(retrySource))
            throw new ArgumentException("RetrySource must not be empty.", nameof(retrySource));
        if (string.IsNullOrWhiteSpace(originalStatus))
            throw new ArgumentException("OriginalStatus must not be empty.", nameof(originalStatus));
        if (attemptNumber < 2)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Retry attempts start at 2.");

        return new TripRetryEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            OriginalTripId = originalTripId,
            NewTripId = newTripId,
            DeliveryOrderId = deliveryOrderId,
            AttemptNumber = attemptNumber,
            OriginalStatus = originalStatus,
            RetrySource = retrySource,
            RetriedBy = string.IsNullOrWhiteSpace(retriedBy) ? null : retriedBy.Trim(),
            RetryReason = string.IsNullOrWhiteSpace(retryReason) ? null : retryReason.Trim(),
            CorrelationId = correlationId
        };
    }
}
