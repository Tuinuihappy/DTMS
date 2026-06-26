namespace DTMS.SharedKernel.Projection;

/// <summary>
/// Per-module inbox repository. Implementations live next to the module's
/// DbContext so reads and writes share the same transactional scope as
/// the read-model row(s) being projected.
///
/// The <see cref="IdempotentProjector{TEvent}"/> base class consumes this
/// interface — module implementations just need to wire a concrete
/// implementation backed by their DbContext.
/// </summary>
public interface IProjectionInboxRepository
{
    /// <summary>
    /// Returns true when the (projector, eventId) pair has already been
    /// recorded — caller short-circuits without projecting.
    /// </summary>
    Task<bool> HasProcessedAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the (projector, eventId) pair. Caller invokes after the
    /// read-model write so a successful SaveChangesAsync persists inbox
    /// + projection atomically.
    /// </summary>
    Task RecordAsync(InboxMessage message, CancellationToken cancellationToken = default);
}
