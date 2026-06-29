using DTMS.SharedKernel.Outbox;

namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Phase S.3 — dispatch shim invoked by <c>MultiPartitionOutboxProcessor</c>
/// when it picks a partitioned outbox row off the queue. The processor
/// is concerned only with "the row exists, it's mine, mark it
/// processed or failed"; the dispatcher decides what "deliver" means
/// for that system (HTTP callback, message queue, log entry).
///
/// <para>Implementations:</para>
/// <list type="bullet">
///   <item><b>LoggingSourceCallbackDispatcher</b> — default, writes a
///         structured log line so we can verify the processor →
///         dispatcher → row-marked-processed loop works end-to-end
///         without needing a live external system. Suitable for dev
///         and integration tests.</item>
///   <item><b>HttpSourceCallbackDispatcher</b> — S.3.1 follow-up.
///         Resolves the system's <c>CallbackBaseUrl</c> + auth from
///         <c>SystemCredentials</c>, wraps the call in
///         <c>PerSystemHttpClient</c> with distributed circuit
///         breaker + Polly retry, and surfaces non-success status
///         codes as exceptions so the outbox marks the row failed
///         and schedules a retry.</item>
/// </list>
/// </summary>
public interface ISourceCallbackDispatcher
{
    /// <summary>
    /// Deliver one outbox row to the system identified by
    /// <see cref="OutboxMessage.PartitionKey"/>. Throw on transient
    /// failure (network blip, 5xx, timeout) so the outbox retries
    /// per its standard policy; return normally on permanent
    /// success.
    /// </summary>
    Task DispatchAsync(string systemKey, OutboxMessage message, CancellationToken ct);
}
