using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Repositories;

public class LoadUnitProfileRepository : ILoadUnitProfileRepository
{
    private readonly FacilityDbContext _db;

    public LoadUnitProfileRepository(FacilityDbContext db) => _db = db;

    public Task<LoadUnitProfile?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _db.LoadUnitProfiles.FirstOrDefaultAsync(p => p.Code == code.ToUpperInvariant(), ct);

    public Task<LoadUnitProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.LoadUnitProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<LoadUnitProfile>> GetAllAsync(CancellationToken ct = default)
        => _db.LoadUnitProfiles.OrderBy(p => p.Code).ToListAsync(ct);

    public Task<List<LoadUnitProfile>> GetByCarrierTypeAsync(string carrierTypeCode, CancellationToken ct = default)
        => _db.LoadUnitProfiles
            .Where(p => p.CarrierTypeCode == carrierTypeCode.ToUpperInvariant())
            .OrderBy(p => p.Code)
            .ToListAsync(ct);

    public async Task AddAsync(LoadUnitProfile profile, CancellationToken ct = default)
        => await _db.LoadUnitProfiles.AddAsync(profile, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
