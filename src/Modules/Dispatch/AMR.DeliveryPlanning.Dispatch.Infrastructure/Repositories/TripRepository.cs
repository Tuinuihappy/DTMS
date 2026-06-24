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
            .Include(t => t.AmrExtension)
            .Include(t => t.Events)
            .Include(t => t.Exceptions)
            .Include(t => t.ProofsOfDelivery)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Trip?> GetByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(upperKey)) return null;

        // IgnoreQueryFilters: RIOT3 vendor callbacks carry no tenant claim.
        // UpperKey is globally unique so cross-tenant leakage isn't a concern.
        return await _context.Trips
            .IgnoreQueryFilters()
            .Include(t => t.AmrExtension)
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.UpperKey == upperKey, cancellationToken);
    }

    public async Task<Trip?> GetByVendorOrderKeyAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vendorOrderKey)) return null;

        // IgnoreQueryFilters: same reasoning as GetByUpperKeyAsync — vendor
        // webhooks have no tenant context. Phase 3b — vendorOrderKey lives
        // on AmrTripExtension; this is an AMR-only lookup (Manual / Fleet
        // never produce a vendor order key).
        return await _context.Trips
            .IgnoreQueryFilters()
            .Include(t => t.AmrExtension)
            .Include(t => t.Events)
            .FirstOrDefaultAsync(
                t => t.AmrExtension != null && t.AmrExtension.VendorOrderKey == vendorOrderKey,
                cancellationToken);
    }

    public async Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        return await _context.Trips
            .Where(t => t.VehicleId == vehicleId && t.Status == TripStatus.InProgress)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Trip>> GetInFlightEnvelopeTripsAsync(DateTime staleCutoffUtc, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: the reconciler runs as a system service with no
        // tenant context.
        return await _context.Trips
            .IgnoreQueryFilters()
            .Where(t => (t.Status == TripStatus.Created
                         || t.Status == TripStatus.InProgress
                         || t.Status == TripStatus.Paused)
                        && t.CreatedAt >= staleCutoffUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> GetRootTripIdAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        // Recursive CTE — single round trip. Chain depth = AttemptNumber
        // (operator-driven; never deep). IgnoreQueryFilters because vendor
        // webhooks fire without tenant context.
        var rootIds = await _context.Database
            .SqlQuery<Guid>($@"
                WITH RECURSIVE chain AS (
                    SELECT ""Id"", ""PreviousAttemptId""
                    FROM dispatch.""Trips""
                    WHERE ""Id"" = {tripId}
                  UNION ALL
                    SELECT t.""Id"", t.""PreviousAttemptId""
                    FROM dispatch.""Trips"" t
                    JOIN chain c ON t.""Id"" = c.""PreviousAttemptId""
                )
                SELECT ""Id"" AS ""Value""
                FROM chain
                WHERE ""PreviousAttemptId"" IS NULL
                LIMIT 1")
            .ToListAsync(cancellationToken);

        // Fallback to the input tripId on broken-chain data so the caller
        // (OMS consumer) still has a usable shipmentId rather than failing.
        return rootIds.Count > 0 ? rootIds[0] : tripId;
    }

    public async Task<List<Trip>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default)
    {
        // Include Events so the order-level full-audit query (Phase 4.2)
        // can fold per-trip execution events into the consolidated stream
        // without an extra round trip. Other call sites tolerate the
        // collection being populated.
        return await _context.Trips
            .IgnoreQueryFilters()
            .Include(t => t.Events)
            .Where(t => t.DeliveryOrderId == deliveryOrderId)
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
            entry.Property(t => t.FailureReason).IsModified = true;
            entry.Property(t => t.VehicleId).IsModified = true;
            // Phase 3b — vendor vehicle identity captured on first
            // TASK_PROCESSING webhook now lives on AmrTripExtension. The
            // navigation save propagates the change without explicit
            // IsModified marking (the change tracker sees it because
            // MarkVendorStarted creates or mutates the extension entity).
            // Vendor snapshot fields — modifiable post-create when the
            // final snapshot consumer captures the terminal-state response.
            entry.Property(t => t.VendorFinalSnapshot).IsModified = true;
            entry.Property(t => t.VendorExpectedCompletionAt).IsModified = true;

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
