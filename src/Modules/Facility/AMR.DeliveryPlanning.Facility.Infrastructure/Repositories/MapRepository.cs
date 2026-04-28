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
        // b.Ignore(m => m.Stations) tells EF not to use Stations as a navigation property,
        // so new stations added to the map's collection are not auto-detected by change tracking.
        // Explicitly attach any detached (new) stations so they get INSERTed on SaveChanges.
        foreach (var station in map.Stations)
        {
            if (_dbContext.Entry(station).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
                _dbContext.Stations.Add(station);
        }

        _dbContext.Maps.Update(map);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
