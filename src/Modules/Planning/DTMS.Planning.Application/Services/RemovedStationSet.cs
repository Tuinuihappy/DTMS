namespace DTMS.Planning.Application.Services;

/// <summary>
/// Which stations a template may still use, keyed both ways a template refers
/// to a station: its route (DTMS station Guid) and its MOVE missions (RIOT3
/// map id + station id). A reference is "removed" when it isn't an ACTIVE
/// station — inactive (RIOT3 removed it) or gone altogether (purged). MOVE
/// targets are only judged on maps DTMS syncs; elsewhere DTMS can't tell.
/// Built by the API layer from Facility's stations and handed to the template
/// queries, so Planning needn't reference Facility.
/// </summary>
public sealed class RemovedStationSet
{
    /// <summary>No station data — nothing is reported.</summary>
    public static readonly RemovedStationSet Unknown = new();

    private readonly bool _known;
    private readonly HashSet<Guid> _liveIds = [];
    private readonly HashSet<(int MapId, int StationId)> _liveVendor = [];
    private readonly HashSet<int> _syncedMaps = [];
    private readonly Dictionary<Guid, string> _labelById = [];
    private readonly Dictionary<(int MapId, int StationId), string> _labelByVendor = [];

    private RemovedStationSet() { }

    /// <param name="stations">Every station on a live map: id, RIOT3 map id (null if the map isn't synced), RIOT3 station id, label, active.</param>
    public RemovedStationSet(IEnumerable<(Guid Id, int? MapId, int? StationId, string Label, bool IsActive)> stations)
    {
        _known = true;
        foreach (var s in stations)
        {
            _labelById[s.Id] = s.Label;
            if (s.IsActive) _liveIds.Add(s.Id);
            if (s.MapId is not { } m) continue;
            _syncedMaps.Add(m);
            if (s.StationId is not { } v) continue;
            // Two rows can share a key (an inactive one and its live re-creation);
            // live wins for the check, the label is only for display.
            _labelByVendor.TryAdd((m, v), s.Label);
            if (s.IsActive) _liveVendor.Add((m, v));
        }
    }

    /// <summary>Labels of the removed stations a template uses, in route-then-mission order; empty when none.</summary>
    public IReadOnlyList<string> Find(Guid? pickupStationId, Guid? dropStationId, IEnumerable<(int? MapId, int? StationId)> moves)
    {
        var found = new List<string>();
        if (!_known) return found;
        void Add(string label)
        {
            if (!found.Contains(label)) found.Add(label);
        }

        foreach (var id in new[] { pickupStationId, dropStationId })
            if (id is { } g && !_liveIds.Contains(g))
                Add(_labelById.GetValueOrDefault(g, "a deleted station"));
        foreach (var (mapId, stationId) in moves)
            if (mapId is { } m && stationId is { } s && _syncedMaps.Contains(m) && !_liveVendor.Contains((m, s)))
                Add(_labelByVendor.GetValueOrDefault((m, s), $"RIOT3 station {s}"));
        return found;
    }
}
