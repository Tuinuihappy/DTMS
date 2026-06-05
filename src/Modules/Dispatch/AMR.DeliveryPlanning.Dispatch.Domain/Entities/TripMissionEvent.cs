using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

/// <summary>
/// Per-mission lifecycle event captured from RIOT3 sub-task webhooks
/// and reconciler polls. One row per (Trip × MissionKey × State) so a
/// mission that goes PROCESSING → FINISHED produces two rows — the
/// chronological audit ops want for the trip detail UI. Idempotent on
/// the natural key so duplicate webhooks or reconciler races are safe.
///
/// PayloadJson is intentionally NOT stored here — full mission detail
/// lives in Trip.VendorRequestSnapshot (frozen at dispatch) and
/// Trip.VendorFinalSnapshot (frozen at terminal). This entity captures
/// the in-between state transitions a single snapshot cannot.
/// </summary>
public sealed class TripMissionEvent : Entity<Guid>
{
    public Guid TripId { get; private set; }

    /// <summary>Sequence position of the mission within the dispatch
    /// request (0-based). Helps render the timeline in order.</summary>
    public int MissionIndex { get; private set; }

    /// <summary>RIOT3-issued missionKey (e.g.
    /// "move6a2224fce4b05147727106b4"). Stable for the life of a
    /// vendor-side order.</summary>
    public string MissionKey { get; private set; } = string.Empty;

    /// <summary>"MOVE" or "ACT".</summary>
    public string MissionType { get; private set; } = string.Empty;

    /// <summary>Destination station for MOVE missions, denormalised
    /// from the vendor payload so the UI can render without joining.</summary>
    public string? StationName { get; private set; }

    /// <summary>ACT action display name.</summary>
    public string? ActionName { get; private set; }

    /// <summary>ACT action type code.</summary>
    public string? ActionType { get; private set; }

    /// <summary>"PROCESSING" | "FINISHED" | "FAILED" | "CANCELED".</summary>
    public string State { get; private set; } = string.Empty;

    public string? ResultCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When the vendor reports the state change happened
    /// (wall-clock on the vendor side).</summary>
    public DateTime ChangeStateTime { get; private set; }

    /// <summary>When DTMS ingested the event (DTMS wall-clock — helps
    /// debug clock skew between vendor and us).</summary>
    public DateTime ReceivedAt { get; private set; }

    private TripMissionEvent() { }

    public static TripMissionEvent Record(
        Guid tripId,
        int missionIndex,
        string missionKey,
        string missionType,
        string state,
        DateTime changeStateTime,
        string? stationName = null,
        string? actionName = null,
        string? actionType = null,
        string? resultCode = null,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(missionKey))
            throw new ArgumentException("MissionKey must not be empty.", nameof(missionKey));
        if (string.IsNullOrWhiteSpace(missionType))
            throw new ArgumentException("MissionType must not be empty.", nameof(missionType));
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State must not be empty.", nameof(state));

        return new TripMissionEvent
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            MissionIndex = missionIndex,
            MissionKey = missionKey.Trim(),
            MissionType = missionType.Trim().ToUpperInvariant(),
            State = state.Trim().ToUpperInvariant(),
            ChangeStateTime = changeStateTime,
            ReceivedAt = DateTime.UtcNow,
            StationName = string.IsNullOrWhiteSpace(stationName) ? null : stationName.Trim(),
            ActionName = string.IsNullOrWhiteSpace(actionName) ? null : actionName.Trim(),
            ActionType = string.IsNullOrWhiteSpace(actionType) ? null : actionType.Trim(),
            ResultCode = string.IsNullOrWhiteSpace(resultCode) ? null : resultCode.Trim(),
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim()
        };
    }
}
