namespace AMR.DeliveryPlanning.Api.RobotPositions;

/// <summary>
/// Singleton in-memory snapshot of the latest known robot positions per map.
/// Written exclusively by <see cref="Riot3PositionPollerService"/>; read by the
/// HTTP endpoint that the frontend polls. Keeps the hot path lock-free so a slow
/// reader can't stall the poller and vice versa.
/// </summary>
public interface IRobotPositionStore
{
    /// <summary>Replace the snapshot with the freshly-polled positions.
    /// Robots absent from <paramref name="positions"/> are dropped — RIOT3
    /// is the source of truth for "what robots exist". Returns the count of
    /// positions retained so the poller can log activity.</summary>
    int ReplaceAll(IEnumerable<RobotPositionDto> positions);

    /// <summary>Snapshot of positions on the given DTMS map. Returns an empty
    /// list when the map has no live robots — never null.</summary>
    IReadOnlyList<RobotPositionDto> GetByMap(Guid mapId);

    /// <summary>Total number of robots tracked across all maps.</summary>
    int Count { get; }
}
