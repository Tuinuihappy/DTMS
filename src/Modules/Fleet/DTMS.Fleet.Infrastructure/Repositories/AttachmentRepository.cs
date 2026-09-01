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

    public Task<List<Attachment>> ListForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default)
        => _db.Attachments
            .AsNoTracking()
            .Where(OwnedBy(owner, ownerId))
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync(ct);

    public Task<int> CountForOwnerAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default)
        => _db.Attachments.CountAsync(OwnedBy(owner, ownerId), ct);

    public Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<List<string>> ListObjectKeysForCarrierAsync(
        Guid carrierId, CancellationToken ct = default)
    {
        var rows = await _db.Attachments
            .AsNoTracking()
            .Where(a => a.CarrierId == carrierId)
            .Select(a => new { a.ObjectKey, a.ThumbnailKey })
            .ToListAsync(ct);

        return rows
            .SelectMany(r => r.ThumbnailKey is null
                ? new[] { r.ObjectKey }
                : [r.ObjectKey, r.ThumbnailKey])
            .ToList();
    }

    public async Task AddAsync(Attachment attachment, CancellationToken ct = default)
        => await _db.Attachments.AddAsync(attachment, ct);

    public void Remove(Attachment attachment) => _db.Attachments.Remove(attachment);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
