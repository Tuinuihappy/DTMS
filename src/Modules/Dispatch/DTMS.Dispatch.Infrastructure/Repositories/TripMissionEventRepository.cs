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

    public async Task<MissionUpsertResult> AddIfNotExistsAsync(TripMissionEvent missionEvent, CancellationToken cancellationToken = default)
    {
        // Fast path — single round trip, identical cost to pre-RC3: the
        // 4-column unique index (TripId, MissionKey, State, Occurrence) is
        // the arbiter and Occurrence=1 covers every first-seen state.
        //
        // Raw SQL deliberately bypasses the change tracker (SaveChangesAsync
        // would flush unrelated tracked entities from the caller's scope) and
        // every statement here is idempotent, so EnableRetryOnFailure replays
        // after a dropped connection are safe.
        var rows = await InsertAsync(missionEvent, occurrence: 1, cancellationToken);
        if (rows > 0) return new MissionUpsertResult(true, 1);

        // Conflict path (rare — duplicate frame or a genuine RIOT retry).
        // One SELECT answers all three questions:
        //   max_occ    — highest stored attempt for this (trip, key, state)
        //   near_any   — is the incoming time within the threshold of ANY
        //                stored attempt? Comparing against every attempt (not
        //                just the latest) is what stops a delayed duplicate of
        //                attempt 1 — arriving after attempt 2 exists — from
        //                minting a phantom attempt 3 (RIOT retries callback
        //                delivery with backoff, so late re-sends are real).
        //   has_failed — has this mission ever FAILED? RIOT only retries
        //                after failure; never-failed missions cannot mint.
        var verdict = await GetConflictVerdictAsync(missionEvent, cancellationToken);
        if (verdict.MaxOccurrence == 0)
        {
            // Conflict on insert but no rows visible: the competing writer's
            // transaction hasn't committed yet. Treat as duplicate — the
            // competing row IS this event, occurrence unknown; report 1.
            return new MissionUpsertResult(false, 1);
        }

        if (!MissionRetryPolicy.IsGenuineRetry(verdict.NearAnyExistingAttempt, verdict.MissionHasFailed))
            return new MissionUpsertResult(false, verdict.MaxOccurrence);

        // Genuine retry — mint the next occurrence. A second conflict here
        // means another writer (webhook vs reconciler carrying the same
        // attempt) beat us to it; re-check once, bounded, no loop.
        var nextOccurrence = verdict.MaxOccurrence + 1;
        rows = await InsertAsync(missionEvent, nextOccurrence, cancellationToken);
        if (rows > 0) return new MissionUpsertResult(true, nextOccurrence);

        var recheck = await GetConflictVerdictAsync(missionEvent, cancellationToken);
        return new MissionUpsertResult(false, Math.Max(recheck.MaxOccurrence, 1));
    }

    private Task<int> InsertAsync(TripMissionEvent missionEvent, int occurrence, CancellationToken cancellationToken)
        => _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dispatch."TripMissionEvents"
                ("Id", "TripId", "MissionIndex", "MissionKey", "MissionType", "State",
                 "StationName", "ActionName", "ActionType", "ResultCode", "ErrorMessage",
                 "ChangeStateTime", "ReceivedAt", "Occurrence")
            VALUES
                ({Guid.NewGuid()}, {missionEvent.TripId}, {missionEvent.MissionIndex},
                 {missionEvent.MissionKey}, {missionEvent.MissionType}, {missionEvent.State},
                 {missionEvent.StationName}, {missionEvent.ActionName}, {missionEvent.ActionType},
                 {missionEvent.ResultCode}, {missionEvent.ErrorMessage},
                 {missionEvent.ChangeStateTime}, {missionEvent.ReceivedAt}, {occurrence})
            ON CONFLICT ("TripId", "MissionKey", "State", "Occurrence") DO NOTHING
            """, cancellationToken);

    private sealed record ConflictVerdict(int MaxOccurrence, bool NearAnyExistingAttempt, bool MissionHasFailed);

    private async Task<ConflictVerdict> GetConflictVerdictAsync(TripMissionEvent missionEvent, CancellationToken cancellationToken)
    {
        var rows = await _context.TripMissionEvents
            .AsNoTracking()
            .Where(e => e.TripId == missionEvent.TripId && e.MissionKey == missionEvent.MissionKey)
            .Select(e => new { e.State, e.Occurrence, e.ChangeStateTime })
            .ToListAsync(cancellationToken);

        var sameState = rows.Where(r => r.State == missionEvent.State).ToList();
        return new ConflictVerdict(
            MaxOccurrence: sameState.Count == 0 ? 0 : sameState.Max(r => r.Occurrence),
            NearAnyExistingAttempt: sameState.Any(r =>
                MissionRetryPolicy.IsSameAttempt(r.ChangeStateTime, missionEvent.ChangeStateTime)),
            MissionHasFailed: rows.Any(r => r.State == "FAILED"));
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
