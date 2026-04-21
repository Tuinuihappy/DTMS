using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;

public class MapRepository : IMapRepository
{
    private readonly FacilityDbContext _dbContext;

    public MapRepository(FacilityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Map?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Maps.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task AddAsync(Map map, CancellationToken cancellationToken = default)
    {
        await _dbContext.Maps.AddAsync(map, cancellationToken);
    }

    public void Update(Map map)
    {
        _dbContext.Maps.Update(map);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
