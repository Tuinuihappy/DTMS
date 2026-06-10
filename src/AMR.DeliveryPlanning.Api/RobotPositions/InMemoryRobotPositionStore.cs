using System.Collections.Concurrent;

namespace AMR.DeliveryPlanning.Api.RobotPositions;

/// <summary>
/// Lock-free snapshot keyed by RIOT3 deviceKey. Writes are full-replace from
/// the poller (no incremental upserts) so the dictionary always reflects the
/// most recent RIOT3 cycle. Reads project a filtered list per map without
/// holding any lock — they accept the possibility of a stale entry from a
/// concurrent ReplaceAll, which is acceptable for a 1 Hz UI feed.
/// </summary>
internal sealed class InMemoryRobotPositionStore : IRobotPositionStore
{
    private readonly ConcurrentDictionary<string, RobotPositionDto> _positions = new();

    public int ReplaceAll(IEnumerable<RobotPositionDto> positions)
    {
        var fresh = positions.ToDictionary(p => p.DeviceKey, p => p);

        // Drop robots that disappeared from the latest poll (offline / despawned).
        foreach (var key in _positions.Keys)
        {
            if (!fresh.ContainsKey(key))
                _positions.TryRemove(key, out _);
        }

        foreach (var (k, v) in fresh)
            _positions[k] = v;

        return _positions.Count;
    }

    public IReadOnlyList<RobotPositionDto> GetByMap(Guid mapId)
    {
        // Snapshot Values once — avoids enumerator invalidation if the poller
        // mutates mid-iteration.
        return _positions.Values
            .Where(p => p.MapId == mapId)
            .ToList();
    }

    public int Count => _positions.Count;
}
