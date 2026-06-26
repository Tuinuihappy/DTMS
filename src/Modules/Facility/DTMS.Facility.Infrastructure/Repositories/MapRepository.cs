using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Repositories;

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

    public Task<Map?> GetByVendorRefAsync(string vendorRef, CancellationToken cancellationToken = default)
    {
        return _dbContext.Maps.FirstOrDefaultAsync(m => m.VendorRef == vendorRef, cancellationToken);
    }

    public async Task<IReadOnlyList<Map>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Maps
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
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
