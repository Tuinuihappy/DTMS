namespace DTMS.SharedKernel.Outbox;

/// <summary>
/// Phase O3 — terminally-failed outbox message. Moved out of the
/// per-module <c>OutboxMessages</c> table into the central
/// <c>outbox.DeadLetterMessages</c> table once
/// <see cref="OutboxMessage.HasReachedMaxRetries"/> flips true. Preserves
/// the original id (via <see cref="OriginalOutboxId"/>) so a replay can
/// reconstruct the source event without losing correlation.
///
/// <para><b>Why a physical table (vs "terminal rows stay in original
/// with ProcessedOnUtc set"):</b> a dedicated table gives ops a first-
/// class place to look ("show me every poison message in the last
/// week") instead of scanning six schemas with the same filter. It
/// also lets the admin replay endpoint delegate to a router that
/// knows which module DbContext to write back to.</para>
///
/// <para><b>Unique constraint on <see cref="OriginalOutboxId"/>.</b>
/// The DLQ move + delete pair is eventually-consistent (two DbContexts,
/// two transactions). If the delete fails after the DLQ insert, the
/// original row stays and next tick re-attempts the insert — the unique
/// constraint makes that idempotent (Postgres returns a UniqueViolation
/// which callers treat as "already moved, proceed to delete").</para>
/// </summary>
public class DeadLetterMessage
{
    /// <summary>DLQ row id (new Guid). Not the original outbox id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Original OutboxMessage.Id. UNIQUE (idempotency key).</summary>
    public Guid OriginalOutboxId { get; private set; }

    /// <summary>
    /// Schema name of the module that owned the original row —
    /// "deliveryorder", "planning", "dispatch", "fleet", "vendoradapter".
    /// Consumed by <c>IDeadLetterReplayRouter</c> to route
    /// <c>ReplayAsync</c> back to the correct DbContext.
    /// </summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>Original .NET type + assembly of the integration event.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Original JSON payload (serialized event).</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>When the domain event occurred (original OccurredOnUtc).</summary>
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>Wall time of the first failed publish attempt.</summary>
    public DateTime FirstFailedOnUtc { get; private set; }

    /// <summary>Wall time of the LAST (terminal) failed attempt.</summary>
    public DateTime LastFailedOnUtc { get; private set; }

    /// <summary>Retry count at time of terminal failure. Always == OutboxRetryPolicy.MaxRetries.</summary>
    public int RetryCount { get; private set; }

    /// <summary>Exception message from the terminal failure.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Phase O4 pre-wiring — W3C traceparent (55 chars) captured at the
    /// original publish attempt so a Jaeger trace can pick up the DLQ
    /// span under the same root. Nullable — populated once O4 lands.
    /// </summary>
    public string? TraceParent { get; private set; }

    private DeadLetterMessage() { } // EF

    public DeadLetterMessage(
        Guid id,
        Guid originalOutboxId,
        string source,
        string type,
        string content,
        DateTime occurredOnUtc,
        DateTime firstFailedOnUtc,
        DateTime lastFailedOnUtc,
        int retryCount,
        string? lastError,
        string? traceParent = null)
    {
        Id = id;
        OriginalOutboxId = originalOutboxId;
        Source = source;
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
        FirstFailedOnUtc = firstFailedOnUtc;
        LastFailedOnUtc = lastFailedOnUtc;
        RetryCount = retryCount;
        LastError = lastError;
        TraceParent = traceParent;
    }
}
