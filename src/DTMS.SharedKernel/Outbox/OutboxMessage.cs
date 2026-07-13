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

    /// <summary>
    /// Phase S.5 (B2) — outbound route override for source callbacks. When set,
    /// the <c>HttpSourceCallbackDispatcher</c> POSTs to
    /// <c>{CallbackBaseUrl}{CallbackPath}</c> instead of the default
    /// <c>/events</c>; NULL = default. The fan-out producer copies these from
    /// the formatter's <c>CallbackPayload</c> (already resolved, no templating).
    /// </summary>
    public string? CallbackPath { get; private set; }

    /// <summary>HTTP verb for the callback (e.g. "POST"). NULL = POST.</summary>
    public string? CallbackMethod { get; private set; }

    /// <summary>
    /// Phase S.5 — the DeliveryOrder / Trip this callback row relates to, so a
    /// dispatch-outcome consumer can write the per-order OMS-notification audit
    /// (the order-detail UI reads it). NULL for callbacks with no order/trip
    /// context. Carried on the row because the outbox row is the only thing the
    /// dispatcher sees at HTTP time.
    /// </summary>
    public Guid? RelatedOrderId { get; private set; }

    public Guid? RelatedTripId { get; private set; }

    public bool HasReachedMaxRetries => RetryCount >= OutboxRetryPolicy.MaxRetries;

    private OutboxMessage() { } // For EF Core

    public OutboxMessage(
        Guid id,
        string type,
        string content,
        DateTime occurredOnUtc,
        string? partitionKey = null,
        Guid? correlationId = null,
        string? traceParent = null,
        string? callbackPath = null,
        string? callbackMethod = null,
        Guid? relatedOrderId = null,
        Guid? relatedTripId = null)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
        PartitionKey = partitionKey;
        CorrelationId = correlationId;
        TraceParent = traceParent;
        CallbackPath = callbackPath;
        CallbackMethod = callbackMethod;
        RelatedOrderId = relatedOrderId;
        RelatedTripId = relatedTripId;
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
