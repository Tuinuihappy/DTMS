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
        // Single round trip: the unique index (TripId, MissionKey, State) is
        // the arbiter, so ON CONFLICT DO NOTHING makes duplicate suppression
        // the database's job — no pre-SELECT, no race window, no
        // DbUpdateException dance. Rows-affected tells us whether we won.
        //
        // Raw SQL deliberately bypasses the change tracker: the old
        // Add + SaveChangesAsync would also flush any OTHER entity the
        // caller's scope happened to be tracking (e.g. a Trip loaded earlier
        // in the same webhook request) — an invisible coupling this method
        // never advertised. The statement is idempotent, so it is safe under
        // EnableRetryOnFailure replaying it after a dropped connection.
        var rows = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dispatch."TripMissionEvents"
                ("Id", "TripId", "MissionIndex", "MissionKey", "MissionType", "State",
                 "StationName", "ActionName", "ActionType", "ResultCode", "ErrorMessage",
                 "ChangeStateTime", "ReceivedAt")
            VALUES
                ({missionEvent.Id}, {missionEvent.TripId}, {missionEvent.MissionIndex},
                 {missionEvent.MissionKey}, {missionEvent.MissionType}, {missionEvent.State},
                 {missionEvent.StationName}, {missionEvent.ActionName}, {missionEvent.ActionType},
                 {missionEvent.ResultCode}, {missionEvent.ErrorMessage},
                 {missionEvent.ChangeStateTime}, {missionEvent.ReceivedAt})
            ON CONFLICT ("TripId", "MissionKey", "State") DO NOTHING
            """, cancellationToken);
        return rows > 0;
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
