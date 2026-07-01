namespace DTMS.SharedKernel.Outbox;

/// <summary>
/// Phase O3 — DLQ port. Called by the outbox processor when a message
/// reaches terminal failure and by admin endpoints for list / replay /
/// delete. Central table <c>outbox.DeadLetterMessages</c> serves all
/// modules.
/// </summary>
public interface IDeadLetterStore
{
    /// <summary>
    /// Insert a DLQ row from a terminally-failed <see cref="OutboxMessage"/>.
    /// Idempotent via unique index on <c>OriginalOutboxId</c> — a re-attempt
    /// after a prior partial success (delete-side failure) is a no-op.
    /// Returns <c>true</c> if a new row was inserted, <c>false</c> if the
    /// row already existed.
    /// </summary>
    Task<bool> MoveAsync(
        OutboxMessage original,
        string source,
        DateTime firstFailedOnUtc,
        DateTime lastFailedOnUtc,
        CancellationToken ct = default);

    /// <summary>Paginated list, newest failure first. Optional source filter.</summary>
    Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        int take,
        int skip,
        string? sourceFilter = null,
        CancellationToken ct = default);

    Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Total count for the metrics gauge.</summary>
    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-emit a DLQ message back into its original module's OutboxMessages
    /// with <c>RetryCount=0</c> + null <c>NextRetryAtUtc</c>. Deletes the
    /// DLQ row on success. At-least-once — if the DLQ delete fails after
    /// the re-emit, admin re-runs the endpoint and it's idempotent
    /// (unique constraint on original ref catches the dup).
    /// </summary>
    Task<bool> ReplayAsync(Guid id, CancellationToken ct = default);

    /// <summary>Hard delete a DLQ row without re-emitting.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
