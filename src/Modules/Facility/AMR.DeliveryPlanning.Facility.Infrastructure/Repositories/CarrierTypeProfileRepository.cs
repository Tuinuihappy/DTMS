using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;

public class CarrierTypeProfileRepository : ICarrierTypeProfileRepository
{
    private readonly FacilityDbContext _db;

    public CarrierTypeProfileRepository(FacilityDbContext db) => _db = db;

    public Task<CarrierTypeProfile?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _db.CarrierTypeProfiles.FirstOrDefaultAsync(c => c.Code == code.ToUpperInvariant(), ct);

    public Task<CarrierTypeProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.CarrierTypeProfiles.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<CarrierTypeProfile>> GetAllAsync(CancellationToken ct = default)
        => _db.CarrierTypeProfiles.OrderBy(c => c.Code).ToListAsync(ct);

    public async Task AddAsync(CarrierTypeProfile profile, CancellationToken ct = default)
        => await _db.CarrierTypeProfiles.AddAsync(profile, ct);

    public Task UpdateAsync(CarrierTypeProfile profile, CancellationToken ct = default)
    {
        _db.CarrierTypeProfiles.Update(profile);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
