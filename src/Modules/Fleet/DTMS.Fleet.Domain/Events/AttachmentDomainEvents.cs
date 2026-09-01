using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Events;

/// <summary>
/// Raised when an attachment row is about to disappear and its stored objects
/// are left with nothing pointing at them.
///
/// <para>The database and the object store are separate systems, so removing
/// the row and removing the bytes cannot be one atomic act. Deleting the bytes
/// first risks a rolled-back transaction leaving a row that points at nothing,
/// which breaks the page; deleting them after the commit with no record risks
/// losing the instruction if the process dies in between. Raising it here means
/// the interceptor writes the instruction to the outbox inside the very
/// transaction that removes the row, so the two agree or neither happens.</para>
/// </summary>
public record AttachmentObjectsOrphanedDomainEvent(
    Guid AttachmentId,
    string Bucket,
    IReadOnlyList<string> ObjectKeys) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
