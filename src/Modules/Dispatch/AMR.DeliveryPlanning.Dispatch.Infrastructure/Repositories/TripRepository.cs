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
        // RobotTasks has no global filter; look up the owning TripId first.
        var tripId = await _context.RobotTasks
            .Where(rt => rt.Id == taskId)
            .Select(rt => rt.TripId)
            .FirstOrDefaultAsync(cancellationToken);

        if (tripId == Guid.Empty) return null;

        // IgnoreQueryFilters: RIOT3 vendor callbacks carry no tenant claim.
        // We look up by TripId (which is RIOT3-internal), so there is no cross-tenant
        // ambiguity — one TaskId maps to exactly one Trip.
        return await _context.Trips
            .IgnoreQueryFilters()
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
        // Direct SQL updates via ExecuteUpdateAsync bypass EF change-tracking.
        await _context.Trips
            .Where(t => t.Id == trip.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, trip.Status)
                .SetProperty(t => t.CompletedAt, trip.CompletedAt),
                cancellationToken);

        foreach (var task in trip.Tasks)
        {
            await _context.RobotTasks
                .Where(rt => rt.Id == task.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(rt => rt.Status, task.Status)
                    .SetProperty(rt => rt.StartedAt, task.StartedAt)
                    .SetProperty(rt => rt.CompletedAt, task.CompletedAt)
                    .SetProperty(rt => rt.FailureReason, task.FailureReason),
                    cancellationToken);
        }

        // Persist new ExecutionEvents added by domain operations (RecordEvent).
        // Detach all tracked entities first to avoid stale snapshot conflicts,
        // then add only new events as fresh Added entities.
        _context.ChangeTracker.Clear();
        foreach (var evt in trip.Events)
        {
            var existing = await _context.ExecutionEvents
                .AsNoTracking()
                .AnyAsync(e => e.Id == evt.Id, cancellationToken);
            if (!existing)
                _context.ExecutionEvents.Add(evt);
        }
        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(cancellationToken);
    }
}
