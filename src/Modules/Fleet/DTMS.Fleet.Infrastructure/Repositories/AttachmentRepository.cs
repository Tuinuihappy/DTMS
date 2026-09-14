using System.Collections.ObjectModel;
using System.Linq.Expressions;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Repositories;

public class AttachmentRepository : IAttachmentRepository
{
    private readonly FleetDbContext _db;

    public AttachmentRepository(FleetDbContext db) => _db = db;

    // One predicate builder for all three owners, so a query can never end up
    // filtering on the wrong column — and each expression matches a filtered
    // index exactly.
    private static Expression<Func<Attachment, bool>> OwnedBy(AttachmentOwner owner, Guid id) => owner switch
    {
        AttachmentOwner.Carrier => a => a.CarrierId == id,
        AttachmentOwner.CarrierType => a => a.CarrierTypeId == id,
        AttachmentOwner.MaintenanceLog => a => a.MaintenanceLogId == id,
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown attachment owner.")
    };

    // The many-owner counterpart of OwnedBy. The explicit "IS NOT NULL" is not
    // redundant: it keeps the predicate identical to each filtered index's own
    // filter, so the planner can use the index for the IN list.
    private static Expression<Func<Attachment, bool>> OwnedByAny(AttachmentOwner owner, Guid[] ids) => owner switch
    {
        AttachmentOwner.Carrier => a => a.CarrierId != null && ids.Contains(a.CarrierId.Value),
        AttachmentOwner.CarrierType => a => a.CarrierTypeId != null && ids.Contains(a.CarrierTypeId.Value),
        AttachmentOwner.MaintenanceLog => a => a.MaintenanceLogId != null && ids.Contains(a.MaintenanceLogId.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown attachment owner.")
    };

    private static Expression<Func<Attachment, ImageStamp>> StampFor(AttachmentOwner owner) => owner switch
    {
        AttachmentOwner.Carrier => a => new ImageStamp(a.CarrierId!.Value, a.Id, a.UploadedAt),
        AttachmentOwner.CarrierType => a => new ImageStamp(a.CarrierTypeId!.Value, a.Id, a.UploadedAt),
        AttachmentOwner.MaintenanceLog => a => new ImageStamp(a.MaintenanceLogId!.Value, a.Id, a.UploadedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown attachment owner.")
    };

    private sealed record ImageStamp(Guid OwnerId, Guid Id, DateTime UploadedAt);

    public Task<List<Attachment>> ListForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default)
        => _db.Attachments
            .AsNoTracking()
            .Where(OwnedBy(owner, ownerId))
            .OrderByDescending(a => a.UploadedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, AttachmentSummary>> GetSummariesForOwnersAsync(
        AttachmentOwner owner, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default)
    {
        if (ownerIds.Count == 0)
            return ReadOnlyDictionary<Guid, AttachmentSummary>.Empty;

        var ids = ownerIds.Distinct().ToArray();

        // Three narrow columns, grouped in memory rather than asking EF to
        // translate "first row per group". The volume is capped by design — at
        // most MaxAttachmentsPerOwner per owner, a list page of at most 200 — so
        // this is a few thousand small rows at worst, and the cover rule stays in
        // one testable place instead of being re-expressed as SQL.
        var stamps = await _db.Attachments
            .Where(OwnedByAny(owner, ids))
            .Select(StampFor(owner))
            .ToListAsync(ct);

        return AttachmentSummary.Summarize(stamps.Select(s => (s.OwnerId, s.Id, s.UploadedAt)));
    }

    public Task<Attachment?> FindForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, Guid id, CancellationToken ct = default)
        => _db.Attachments
            .AsNoTracking()
            .Where(OwnedBy(owner, ownerId))
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<int> CountForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default)
        => _db.Attachments.CountAsync(OwnedBy(owner, ownerId), ct);

    public Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);

    // No AsNoTracking, unlike every other read here: the caller raises a domain
    // event on each row and the interceptor only drains tracked aggregates.
    public Task<List<Attachment>> ListForCarrierForDeleteAsync(
        Guid carrierId, CancellationToken ct = default)
        => _db.Attachments
            .Where(a => a.CarrierId == carrierId)
            .ToListAsync(ct);

    public async Task AddAsync(Attachment attachment, CancellationToken ct = default)
        => await _db.Attachments.AddAsync(attachment, ct);

    public void Remove(Attachment attachment) => _db.Attachments.Remove(attachment);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
