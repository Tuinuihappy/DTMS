using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Repositories;

public class VehicleTypeRepository : IVehicleTypeRepository
{
    private readonly FleetDbContext _dbContext;

    public VehicleTypeRepository(FleetDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<VehicleType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.VehicleTypes.FirstOrDefaultAsync(vt => vt.Id == id, cancellationToken);
    }

    public async Task AddAsync(VehicleType vehicleType, CancellationToken cancellationToken = default)
    {
        await _dbContext.VehicleTypes.AddAsync(vehicleType, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
