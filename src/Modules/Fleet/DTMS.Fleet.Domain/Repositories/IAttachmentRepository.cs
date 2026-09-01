using DTMS.Fleet.Domain.Entities;

namespace DTMS.Fleet.Domain.Repositories;

public interface IAttachmentRepository
{
    /// <summary>Newest first.</summary>
    Task<List<Attachment>> ListForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default);

    /// <summary>Feeds the per-owner ceiling. Checked at presign to fail early and
    /// again at confirm, though two concurrent confirms can still both pass —
    /// see the confirm handler for why that is left alone.</summary>
    Task<int> CountForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default);

    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Every object key belonging to one carrier, read <b>before</b> the carrier
    /// is deleted.
    ///
    /// <para>The database cascade removes these rows but cannot touch object
    /// storage, and because it runs below EF it raises no domain event either —
    /// so nothing downstream would ever learn the bytes were abandoned. The keys
    /// have to be collected while the rows still exist.</para>
    /// </summary>
    Task<List<string>> ListObjectKeysForCarrierAsync(Guid carrierId, CancellationToken ct = default);

    Task AddAsync(Attachment attachment, CancellationToken ct = default);
    void Remove(Attachment attachment);
    Task SaveChangesAsync(CancellationToken ct = default);
}
