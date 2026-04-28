using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Repositories;

public class TripRepository : ITripRepository
{
    private readonly DispatchDbContext _context;

    public TripRepository(DispatchDbContext context)
    {
        _context = context;
    }

    public async Task<Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Trips
            .Include(t => t.Tasks)
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Trip?> GetTripByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var tripId = await _context.RobotTasks
            .Where(rt => rt.Id == taskId)
            .Select(rt => rt.TripId)
            .FirstOrDefaultAsync(cancellationToken);

        if (tripId == Guid.Empty) return null;

        return await _context.Trips
            .Include(t => t.Tasks)
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
    }

    public async Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        return await _context.Trips
            .Where(t => t.VehicleId == vehicleId && t.Status == TripStatus.InProgress)
            .Include(t => t.Tasks)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        await _context.Trips.AddAsync(trip, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        _context.Trips.Update(trip);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
