using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Dispatch.Infrastructure.Repositories;

public sealed class TripMissionEventRepository : ITripMissionEventRepository
{
    private readonly DispatchDbContext _context;

    public TripMissionEventRepository(DispatchDbContext context)
    {
        _context = context;
    }

    public async Task<bool> AddIfNotExistsAsync(TripMissionEvent missionEvent, CancellationToken cancellationToken = default)
    {
        // Race-safe duplicate suppression: pre-check is best-effort, the
        // unique index is the actual guarantee. We swallow the resulting
        // unique-violation so concurrent webhook + reconciler writes both
        // appear to succeed from the caller's perspective.
        var exists = await _context.TripMissionEvents
            .IgnoreQueryFilters()
            .AnyAsync(e =>
                e.TripId == missionEvent.TripId &&
                e.MissionKey == missionEvent.MissionKey &&
                e.State == missionEvent.State,
                cancellationToken);
        if (exists) return false;

        await _context.TripMissionEvents.AddAsync(missionEvent, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another writer beat us — the unique constraint enforces the
            // invariant. Detach so the next save doesn't retry this entity.
            _context.Entry(missionEvent).State = EntityState.Detached;
            return false;
        }
    }

    public Task<List<TripMissionEvent>> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default)
        // Order by the real vendor state-change time, NOT MissionIndex. Index
        // is authoritative only on reconciler (order-query) rows; sub-task
        // webhook rows hardcode 0 (the payload carries no index), so an
        // index-first sort scrambled the timeline and mislabeled every webhook
        // mission as "#1". ChangeStateTime is real from both sources. ReceivedAt
        // breaks ties deterministically; MissionIndex is a last-resort tiebreak.
        => _context.TripMissionEvents
            .Where(e => e.TripId == tripId)
            .OrderBy(e => e.ChangeStateTime)
            .ThenBy(e => e.ReceivedAt)
            .ThenBy(e => e.MissionIndex)
            .ToListAsync(cancellationToken);
}
