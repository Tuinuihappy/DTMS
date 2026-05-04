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
            .Include(t => t.Exceptions)
            .Include(t => t.ProofsOfDelivery)
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
            .Include(t => t.Exceptions)
            .Include(t => t.ProofsOfDelivery)
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
        var trackedEntry = _context.ChangeTracker
            .Entries<Trip>()
            .FirstOrDefault(e => e.Entity.Id == trip.Id);

        if (trackedEntry == null)
        {
            _context.Trips.Attach(trip);
            var entry = _context.Entry(trip);
            entry.Property(t => t.Status).IsModified = true;
            entry.Property(t => t.StartedAt).IsModified = true;
            entry.Property(t => t.CompletedAt).IsModified = true;

            foreach (var task in trip.Tasks)
            {
                _context.RobotTasks.Attach(task);
                var taskEntry = _context.Entry(task);
                taskEntry.Property(t => t.Status).IsModified = true;
                taskEntry.Property(t => t.StartedAt).IsModified = true;
                taskEntry.Property(t => t.CompletedAt).IsModified = true;
                taskEntry.Property(t => t.FailureReason).IsModified = true;
            }

            foreach (var exception in trip.Exceptions)
            {
                _context.TripExceptions.Attach(exception);
                var exceptionEntry = _context.Entry(exception);
                exceptionEntry.Property(e => e.Resolution).IsModified = true;
                exceptionEntry.Property(e => e.ResolvedBy).IsModified = true;
                exceptionEntry.Property(e => e.ResolvedAt).IsModified = true;
            }
        }

        await AddNewExecutionEventsAsync(trip, cancellationToken);
        await AddNewTripExceptionsAsync(trip, cancellationToken);
        await AddNewProofsOfDeliveryAsync(trip, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task AddNewExecutionEventsAsync(Trip trip, CancellationToken cancellationToken)
    {
        foreach (var executionEvent in trip.Events)
        {
            var entry = _context.Entry(executionEvent);
            if (entry.State == EntityState.Added)
                continue;

            var exists = await _context.ExecutionEvents
                .AnyAsync(e => e.Id == executionEvent.Id, cancellationToken);
            if (!exists)
                entry.State = EntityState.Added;
        }
    }

    private async Task AddNewTripExceptionsAsync(Trip trip, CancellationToken cancellationToken)
    {
        foreach (var exception in trip.Exceptions)
        {
            var entry = _context.Entry(exception);
            if (entry.State == EntityState.Added)
                continue;

            var exists = await _context.TripExceptions
                .AnyAsync(e => e.Id == exception.Id, cancellationToken);
            if (!exists)
                entry.State = EntityState.Added;
        }
    }

    private async Task AddNewProofsOfDeliveryAsync(Trip trip, CancellationToken cancellationToken)
    {
        foreach (var proofOfDelivery in trip.ProofsOfDelivery)
        {
            var entry = _context.Entry(proofOfDelivery);
            if (entry.State == EntityState.Added)
                continue;

            var exists = await _context.ProofsOfDelivery
                .AnyAsync(p => p.Id == proofOfDelivery.Id, cancellationToken);
            if (!exists)
                entry.State = EntityState.Added;
        }
    }
}
