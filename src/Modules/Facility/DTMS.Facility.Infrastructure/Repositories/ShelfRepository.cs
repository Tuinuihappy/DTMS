using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Enums;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;

public class ShelfRepository : IShelfRepository
{
    private readonly FacilityDbContext _db;

    public ShelfRepository(FacilityDbContext db) => _db = db;

    public Task<Shelf?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Shelves.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Shelf?> GetByRfidAsync(string rfid, CancellationToken ct = default)
        => _db.Shelves.FirstOrDefaultAsync(s => s.Rfid == rfid, ct);

    public Task<List<Shelf>> GetByMapAsync(Guid mapId, CancellationToken ct = default)
        => _db.Shelves.Where(s => s.MapId == mapId).ToListAsync(ct);

    public Task<List<Shelf>> GetAllAvailableAsync(double requiredWeightKg, int requiredSlots, CancellationToken ct = default)
        => _db.Shelves
            .Where(s => s.Status == ShelfStatus.Available
                && s.MaxWeightKg >= requiredWeightKg
                && s.MaxSlots >= requiredSlots)
            .ToListAsync(ct);

    public async Task AddAsync(Shelf shelf, CancellationToken ct = default)
        => await _db.Shelves.AddAsync(shelf, ct);

    public Task UpdateAsync(Shelf shelf, CancellationToken ct = default)
    {
        _db.Shelves.Update(shelf);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
