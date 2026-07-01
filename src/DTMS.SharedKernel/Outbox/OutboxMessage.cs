namespace DTMS.SharedKernel.Outbox;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    /// <summary>
    /// Phase S.3 — when set, picks the row out of the default
    /// processor's queue and routes it to a per-system worker in
    /// <c>MultiPartitionOutboxProcessor</c>. NULL means "domain
    /// event, dispatch via MassTransit through the legacy
    /// <c>OutboxProcessorService</c>".
    /// </summary>
    public string? PartitionKey { get; private set; }

    /// <summary>
    /// Phase S.3.1b — populated by the fan-out producer with the source
    /// integration event's <c>MessageId</c>. Paired with
    /// <see cref="PartitionKey"/> by a partial unique index so a consumer
    /// retry (which re-emits the same event) cannot enqueue duplicate
    /// callback rows for the same (system, event) pair. NULL for the
    /// legacy domain-event path; the partial index ignores NULL rows.
    /// </summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>
    /// Phase O4 — W3C traceparent (55 chars, format
    /// <c>00-{32-hex}-{16-hex}-{2-hex}</c>) captured at outbox-write time
    /// from the ambient <see cref="System.Diagnostics.Activity.Current"/>.
    /// Restored by <c>OutboxProcessorService.PublishBatchAsync</c> so the
    /// consumer's Activity chains under the original request's trace —
    /// Jaeger renders one connected waterfall from HTTP POST to SignalR
    /// broadcast, even across the async gap (retry, worker-container jump).
    /// </summary>
    public string? TraceParent { get; private set; }

    public bool HasReachedMaxRetries => RetryCount >= OutboxRetryPolicy.MaxRetries;

    private OutboxMessage() { } // For EF Core

    public OutboxMessage(
        Guid id,
        string type,
        string content,
        DateTime occurredOnUtc,
        string? partitionKey = null,
        Guid? correlationId = null,
        string? traceParent = null)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
        PartitionKey = partitionKey;
        CorrelationId = correlationId;
        TraceParent = traceParent;
    }

    public void MarkAsProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
        NextRetryAtUtc = null;
    }

    public void MarkAsFailed(DateTime attemptedAtUtc, string error)
    {
        RetryCount++;
        Error = error;

        var nextDelay = OutboxRetryPolicy.GetNextRetryDelay(RetryCount);
        if (nextDelay.HasValue)
        {
            NextRetryAtUtc = attemptedAtUtc.Add(nextDelay.Value);
        }
        else
        {
            // Max retries reached — terminal failure; stop polling this row.
            ProcessedOnUtc = attemptedAtUtc;
            NextRetryAtUtc = null;
        }
    }
}
