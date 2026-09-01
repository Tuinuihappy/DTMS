using DTMS.Fleet.Domain.Events;
using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Entities;

/// <summary>What an attachment hangs off. Kept as an enum so callers name the
/// owner rather than setting one of three columns and hoping.</summary>
public enum AttachmentOwner
{
    Carrier,
    CarrierType,
    MaintenanceLog
}

/// <summary>
/// One image belonging to exactly one Fleet thing (ADR-019).
///
/// <para><b>Why three nullable columns instead of (OwnerType, OwnerId).</b> A
/// polymorphic owner cannot carry a foreign key, and P1 deletes carriers for
/// real — so every delete would silently strand rows, with nothing in the
/// database able to object. Naming each owner keeps real FKs and real cascade.
/// Exactly one is set, enforced both here and by a CHECK constraint; adding a
/// fourth owner is a column, an FK, and one more line in that constraint.</para>
///
/// <para>The general capability lives in the mechanism — storage, presign,
/// validation, endpoints, UI — not in this table, so another module wanting
/// attachments builds the same small shape in its own schema and reuses all of
/// it.</para>
/// </summary>
/// <remarks>
/// An aggregate root, not a plain entity, because it has its own lifecycle —
/// added and removed on its own, addressed by its own id — and because removing
/// one has to reach outside the database. Only aggregate roots' domain events
/// are drained into the outbox, and that drain is what keeps "row gone" and
/// "bytes gone" in one transaction.
/// </remarks>
public class Attachment : AggregateRoot<Guid>
{
    /// <summary>Key of the full-size object, after promotion out of staging.</summary>
    public string ObjectKey { get; private set; } = string.Empty;

    /// <summary>
    /// Downscaled copy for lists and grids. Nullable because the browser
    /// produces it and can fail to decode some inputs — a gallery falls back to
    /// the full image rather than showing nothing.
    /// </summary>
    public string? ThumbnailKey { get; private set; }

    public string Bucket { get; private set; } = string.Empty;

    /// <summary>
    /// Pinned by the upload policy, so this is the server's value rather than
    /// the uploader's claim. It is also what the download URL forces the object
    /// to be served as.
    /// </summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>Measured by the storage server, not reported by the client.</summary>
    public long SizeBytes { get; private set; }

    public string? OriginalFileName { get; private set; }
    public string? Caption { get; private set; }

    public DateTime UploadedAt { get; private set; }
    public string UploadedBy { get; private set; } = string.Empty;

    public Guid? CarrierId { get; private set; }
    public Guid? CarrierTypeId { get; private set; }
    public Guid? MaintenanceLogId { get; private set; }

    public AttachmentOwner Owner =>
        CarrierId is not null ? AttachmentOwner.Carrier
        : CarrierTypeId is not null ? AttachmentOwner.CarrierType
        : AttachmentOwner.MaintenanceLog;

    public Guid OwnerId =>
        CarrierId ?? CarrierTypeId ?? MaintenanceLogId
        ?? throw new InvalidOperationException("Attachment has no owner.");

    private Attachment() { }

    public static Attachment For(
        AttachmentOwner owner,
        Guid ownerId,
        string bucket,
        string objectKey,
        string? thumbnailKey,
        string contentType,
        long sizeBytes,
        string? originalFileName,
        string? caption,
        string uploadedBy)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("OwnerId must not be empty.", nameof(ownerId));
        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("Bucket is required.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("ObjectKey is required.", nameof(objectKey));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("ContentType is required.", nameof(contentType));
        if (sizeBytes <= 0)
            throw new ArgumentException("SizeBytes must be positive.", nameof(sizeBytes));

        var a = new Attachment
        {
            Id = Guid.NewGuid(),
            Bucket = bucket.Trim(),
            ObjectKey = objectKey.Trim(),
            ThumbnailKey = Trimmed(thumbnailKey),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            OriginalFileName = Trimmed(originalFileName),
            Caption = Trimmed(caption),
            UploadedAt = DateTime.UtcNow,
            // Never empty: the caller resolves DisplayName → PrincipalId →
            // "system" first. A NOT NULL column takes "" happily and leaves a
            // row that records nothing.
            UploadedBy = string.IsNullOrWhiteSpace(uploadedBy) ? "system" : uploadedBy.Trim()
        };

        // Assigning through the switch is what makes the arc exclusive by
        // construction — there is no path that sets two.
        switch (owner)
        {
            case AttachmentOwner.Carrier: a.CarrierId = ownerId; break;
            case AttachmentOwner.CarrierType: a.CarrierTypeId = ownerId; break;
            case AttachmentOwner.MaintenanceLog: a.MaintenanceLogId = ownerId; break;
            default: throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown attachment owner.");
        }

        return a;
    }

    public void Recaption(string? caption) => Caption = Trimmed(caption);

    /// <summary>
    /// Announces that this row's objects are about to be left unreferenced.
    /// Call it <b>before</b> removing the row: the interceptor reads domain
    /// events off tracked entries, so the instruction and the deletion land in
    /// the same transaction.
    /// </summary>
    public void MarkObjectsOrphaned() =>
        AddDomainEvent(new AttachmentObjectsOrphanedDomainEvent(Id, Bucket, AllObjectKeys()));

    /// <summary>Every object this row owns — what has to be removed from storage
    /// when it goes away.</summary>
    public IReadOnlyList<string> AllObjectKeys() =>
        ThumbnailKey is null ? [ObjectKey] : [ObjectKey, ThumbnailKey];

    private static string? Trimmed(string? v) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
