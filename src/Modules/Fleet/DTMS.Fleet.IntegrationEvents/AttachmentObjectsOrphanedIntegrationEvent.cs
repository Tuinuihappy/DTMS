using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.IntegrationEvents;

/// <summary>
/// Objects whose rows are gone and which nothing will ever reference again.
///
/// <para>Deleting the row and deleting the bytes cannot be one atomic act — the
/// database and the object store are different systems. Doing storage first
/// risks a rolled-back transaction leaving a row pointing at nothing, which
/// breaks the UI; doing it after the commit without a record risks losing the
/// instruction if the process dies. So the instruction is written inside the
/// same transaction as the delete and carried by the outbox, which already
/// gives retries and a dead-letter queue.</para>
///
/// <para>Deleting an object that is already gone must count as success — this
/// is delivered at least once, and a second attempt is normal.</para>
/// </summary>
public record AttachmentObjectsOrphanedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    string Bucket,
    IReadOnlyList<string> ObjectKeys) : IIntegrationEvent;
