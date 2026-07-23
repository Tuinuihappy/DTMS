using DTMS.Dispatch.Domain.Entities;

namespace DTMS.Dispatch.Domain.Repositories;

/// <summary>
/// Outcome of an idempotent mission-event upsert. Inserted=true means a new
/// row was persisted with the given Occurrence (1 for a first-seen state,
/// 2..n for a genuine RIOT-side retry of the same mission state).
/// Inserted=false means the duplicate guard swallowed the write; Occurrence
/// then reports the highest existing attempt so callers can still log it.
/// </summary>
public readonly record struct MissionUpsertResult(bool Inserted, int Occurrence);

/// <summary>
/// RC3 duplicate-vs-retry rules, expressed as domain semantics so the
/// repository applies them and tests exercise them without a database.
/// </summary>
public static class MissionRetryPolicy
{
    /// <summary>The SAME attempt can be reported with timestamps ±1s apart
    /// between the notify webhook and the reconciler's order-GET (observed
    /// on RIOT order 5106: 05:57:15 vs 05:57:16), so anything within 5s of a
    /// stored attempt is a re-report, not a retry. Genuine retries observed
    /// so far restart 47s–10min after the failure.</summary>
    public const double SameAttemptWindowSeconds = 5;

    /// <summary>True when two state-change times belong to the same attempt
    /// (re-reported by the other ingest path or a vendor re-delivery).</summary>
    public static bool IsSameAttempt(DateTime a, DateTime b)
        => Math.Abs((a - b).TotalSeconds) <= SameAttemptWindowSeconds;

    /// <summary>A conflict mints a NEW occurrence only when the incoming
    /// time is far from EVERY stored attempt (comparing against all of them
    /// stops a delayed duplicate of attempt 1 — arriving after attempt 2
    /// exists — from minting a phantom attempt 3) AND the mission has
    /// already FAILED: RIOT only retries after a failure, so a far-off
    /// re-emission on a never-failed mission is a clock artifact
    /// (UtcNow-fallback timestamps, resume-after-pause re-emissions), never
    /// a genuine attempt.</summary>
    public static bool IsGenuineRetry(bool nearAnyExistingAttempt, bool missionHasFailed)
        => !nearAnyExistingAttempt && missionHasFailed;
}

/// <summary>
/// Per-mission audit events. Append-only from the caller's perspective —
/// duplicate (TripId, MissionKey, State, Occurrence) tuples are silently
/// no-op'd by the unique constraint so webhook + reconciler can both write
/// without coordination. RC3: a re-emission whose ChangeStateTime differs
/// from EVERY stored attempt by more than the retry threshold — on a
/// mission that has already FAILED — is stored as a new occurrence instead
/// of being dropped, making RIOT's own retries visible in the timeline.
/// Reads serve the trip detail UI and compliance queries.
/// </summary>
public interface ITripMissionEventRepository
{
    /// <summary>
    /// Idempotent insert with retry detection. Fast path (state never seen
    /// for this mission) is a single INSERT .. ON CONFLICT DO NOTHING.
    /// On conflict, decides duplicate-vs-retry (see implementation for the
    /// evidence-backed rules) and either swallows or mints occurrence n+1.
    /// </summary>
    Task<MissionUpsertResult> AddIfNotExistsAsync(TripMissionEvent missionEvent, CancellationToken cancellationToken = default);

    Task<List<TripMissionEvent>> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default);
}
