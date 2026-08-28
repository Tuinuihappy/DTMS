using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Repositories;

public class CarrierTypeRepository : ICarrierTypeRepository
{
    private readonly FleetDbContext _db;

    public CarrierTypeRepository(FleetDbContext db) => _db = db;

    public Task<CarrierType?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _db.CarrierTypes.FirstOrDefaultAsync(c => c.Code == code.ToUpperInvariant(), ct);

    public Task<CarrierType?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.CarrierTypes.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<CarrierType>> GetAllAsync(CancellationToken ct = default)
        => _db.CarrierTypes.OrderBy(c => c.Code).ToListAsync(ct);

    public async Task AddAsync(CarrierType carrierType, CancellationToken ct = default)
        => await _db.CarrierTypes.AddAsync(carrierType, ct);

    public Task UpdateAsync(CarrierType carrierType, CancellationToken ct = default)
    {
        _db.CarrierTypes.Update(carrierType);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
