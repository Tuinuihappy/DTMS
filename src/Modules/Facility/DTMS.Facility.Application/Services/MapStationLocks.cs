using System.Collections.Concurrent;

namespace DTMS.Facility.Application.Services;

/// <summary>
/// One lock per map shared by everything that rewrites a map's station rows
/// from RIOT3 — the sync (poller + manual) and the purge — so a purge can't
/// delete a row the sync is taking over, and two syncs can't race on the
/// unique indexes. Single api process (the poller only runs there).
/// </summary>
internal static class MapStationLocks
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static SemaphoreSlim For(Guid mapId) => Locks.GetOrAdd(mapId, _ => new SemaphoreSlim(1, 1));
}
