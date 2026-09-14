using DTMS.Fleet.Domain.Entities;

namespace DTMS.Fleet.Domain.Repositories;

public interface IAttachmentRepository
{
    /// <summary>Newest first; ties broken by id, the same order
    /// <see cref="AttachmentSummary.Summarize"/> uses to pick a cover.</summary>
    Task<List<Attachment>> ListForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default);

    /// <summary>
    /// Cover and count for every owner in <paramref name="ownerIds"/>, in one
    /// query — what a list page needs so no row fetches its own images. Owners
    /// with no images are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AttachmentSummary>> GetSummariesForOwnersAsync(
        AttachmentOwner owner, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default);

    /// <summary>
    /// One image, untracked, but only if it belongs to that owner. Null otherwise
    /// — including when the id exists under a different owner, which is what
    /// stops a route guarded by one owner's permission from serving another's.
    /// </summary>
    Task<Attachment?> FindForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>Feeds the per-owner ceiling. Checked at presign to fail early and
    /// again at confirm, though two concurrent confirms can still both pass —
    /// see the confirm handler for why that is left alone.</summary>
    Task<int> CountForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default);

    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// One carrier's images, <b>tracked</b>, for the moment before the carrier
    /// is deleted.
    ///
    /// <para>Tracked and returning entities rather than keys, because each one
    /// has to announce its own objects as orphaned and only a tracked aggregate
    /// gets drained into the outbox. The database cascade would remove these
    /// rows without any of that: it runs below EF, so no domain event fires and
    /// nothing downstream ever learns the bytes were abandoned.</para>
    ///
    /// <para>Separate from <see cref="ListForOwnerAsync"/>, which is a read path
    /// and deliberately untracked.</para>
    /// </summary>
    Task<List<Attachment>> ListForCarrierForDeleteAsync(Guid carrierId, CancellationToken ct = default);

    Task AddAsync(Attachment attachment, CancellationToken ct = default);
    void Remove(Attachment attachment);
    Task SaveChangesAsync(CancellationToken ct = default);
}
