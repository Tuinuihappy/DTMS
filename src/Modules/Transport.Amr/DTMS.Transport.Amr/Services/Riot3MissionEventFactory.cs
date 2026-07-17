using DTMS.Dispatch.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Amr.Services;

/// <summary>
/// Single translation point from raw RIOT3 mission/sub-task fields to a
/// <see cref="TripMissionEvent"/>. BOTH ingest paths — the sub-task webhook
/// (Riot3Webhooks.HandleSubTaskEvent) and the reconciler order-GET poll
/// (Riot3ReconciliationService.UpsertMissionsAsync) — must build rows through
/// here so the two sources can never drift apart semantically again. The
/// first-write-wins dedup at the (TripId, MissionKey, State) unique index is
/// only harmless when both writers produce identical values.
///
/// Field semantics (proven against a real order's terminal record — see the
/// trip 5018 investigation, 2026-07-17):
///
///   • Station is trustworthy ONLY on MOVE missions. RIOT3's own order record
///     gives ACT missions no stationName; the station object that rides on ACT
///     notify frames is a stale "last registered dock" that lags the robot by
///     one leg. Storing it mislabeled every ACT once the route changed
///     stations. ACT rows therefore always get a null station (the UI falls
///     back to actionName).
///
///   • The state-change time must be picked BY STATE, never as a blanket
///     finishedTime ?? startedTime:
///       PROCESSING          → startedTime. A late/retried PROCESSING frame
///                             built after the mission finished carries a
///                             populated finishedTime — using it would place
///                             the row at the mission's END.
///       FINISHED/FAILED/    → finishedTime. A FAILED frame typically has NO
///       CANCELED              finishedTime; falling back to startedTime
///                             stamped one real failure 6.5 minutes early
///                             (E230001, exactly tying the PROCESSING row).
///                             ReceivedAt-now is the honest fallback: it is
///                             when we learned the mission reached the state.
/// </summary>
public static class Riot3MissionEventFactory
{
    public static TripMissionEvent Create(
        Guid tripId,
        int missionIndex,
        string missionKey,
        string? missionType,
        string state,
        string? startedTime,
        string? finishedTime,
        string? stationName,
        string? actionName,
        string? actionType,
        string? resultCode,
        string? errorMessage,
        ILogger logger)
    {
        var type = string.IsNullOrWhiteSpace(missionType) ? "UNKNOWN" : missionType.Trim().ToUpperInvariant();
        var normalizedState = state.Trim().ToUpperInvariant();

        // Station only on MOVE — see class doc. Keep the discarded value in
        // the debug log so a future RIOT3 deployment that DOES bind stations
        // to ACT leaves evidence instead of silently losing data.
        var station = stationName;
        if (type != "MOVE" && !string.IsNullOrWhiteSpace(stationName))
        {
            logger.LogDebug(
                "RIOT3 {Type} mission {MissionKey} carried station '{Station}' — discarded (station is only trustworthy on MOVE; see Riot3MissionEventFactory)",
                type, missionKey, stationName);
            station = null;
        }

        var changeTime = normalizedState switch
        {
            "PROCESSING" => ParseRiot3Time(startedTime),
            _            => ParseRiot3Time(finishedTime)
        } ?? DateTime.UtcNow;

        return TripMissionEvent.Record(
            tripId: tripId,
            missionIndex: missionIndex,
            missionKey: missionKey,
            missionType: type,
            state: normalizedState,
            changeStateTime: changeTime,
            stationName: station,
            actionName: actionName,
            actionType: actionType,
            resultCode: resultCode,
            errorMessage: errorMessage);
    }

    /// <summary>RIOT3 timestamps come as strings; tolerate missing or
    /// malformed. Shared by both ingest paths and the reconciler's
    /// pickup/drop actedAt resolution.</summary>
    public static DateTime? ParseRiot3Time(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt)
            ? dt
            : null;
    }
}
