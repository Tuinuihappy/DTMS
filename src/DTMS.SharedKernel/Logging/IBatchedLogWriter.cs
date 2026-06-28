namespace DTMS.SharedKernel.Logging;

/// <summary>
/// Non-blocking producer for high-throughput log/audit streams. Use this
/// from request-path code that must not pay a synchronous round-trip per
/// entry — the entry is enqueued into a bounded in-process channel and
/// flushed in batches by a paired background drain service.
/// </summary>
/// <remarks>
/// Backpressure policy is "drop oldest": when the channel is full, the
/// oldest entry is evicted to make room. This prefers freshness over
/// completeness because the producers are typically middleware that
/// cannot afford to block. Dropped entries are counted in the
/// <c>dtms.logwriter.dropped</c> metric so the operator can detect
/// sustained overflow.
/// </remarks>
public interface IBatchedLogWriter<in T>
{
    /// <summary>
    /// Enqueues an entry. Returns <c>true</c> if accepted, <c>false</c>
    /// if the channel rejected it (which under the drop-oldest policy
    /// happens only after writer completion). Callers should not block
    /// on this method.
    /// </summary>
    bool Enqueue(T entry);
}
