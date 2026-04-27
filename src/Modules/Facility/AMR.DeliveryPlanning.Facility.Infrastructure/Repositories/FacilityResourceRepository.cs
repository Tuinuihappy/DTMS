using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;

public class FacilityResourceRepository : IFacilityResourceRepository
{
    private readonly FacilityDbContext _db;
    public FacilityResourceRepository(FacilityDbContext db) => _db = db;

    public Task<FacilityResource?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.FacilityResources.FindAsync(new object[] { id }, ct).AsTask();

    public Task<List<FacilityResource>> GetByMapAsync(Guid mapId, CancellationToken ct = default)
        => _db.FacilityResources.Where(r => r.MapId == mapId).ToListAsync(ct);

    public async Task AddAsync(FacilityResource resource, CancellationToken ct = default)
        => await _db.FacilityResources.AddAsync(resource, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
