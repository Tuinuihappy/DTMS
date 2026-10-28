using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Facility.Application.Services;
using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Adapters;

// Composition-root bridge: Facility's purge asks whether Planning, Dispatch
// or DeliveryOrder still point at a station — by its Guid, or (template MOVE
// missions) by its RIOT3 map + station id. Read-only, three small queries.
internal sealed class StationUsageReader : IStationUsageReader
{
    // Trips/orders in these states never dispatch again, so a station they
    // reference is history only. Failed orders stay "in use" — RetryJob can
    // re-dispatch them by their stored station ids.
    private static readonly TripStatus[] FinishedTrips =
        [TripStatus.Completed, TripStatus.Failed, TripStatus.Cancelled, TripStatus.Rejected];
    private static readonly OrderStatus[] FinishedOrders =
        [OrderStatus.Completed, OrderStatus.PartiallyCompleted, OrderStatus.Cancelled, OrderStatus.Rejected];

    private readonly PlanningDbContext _planning;
    private readonly DispatchDbContext _dispatch;
    private readonly DeliveryOrderDbContext _orders;

    public StationUsageReader(PlanningDbContext planning, DispatchDbContext dispatch, DeliveryOrderDbContext orders)
    {
        _planning = planning;
        _dispatch = dispatch;
        _orders = orders;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> FindInUseAsync(
        IReadOnlyCollection<StationUsageCandidate> stations, CancellationToken cancellationToken = default)
    {
        var candidates = stations.DistinctBy(s => s.Id).ToList();
        var ids = candidates.Select(s => s.Id).ToList();
        var inUse = new Dictionary<Guid, string>();
        if (ids.Count == 0) return inUse;

        // Missions is value-converted jsonb that LINQ can't filter on, so every
        // active template is read and matched here — a few hundred small rows.
        var templates = await _planning.OrderTemplates.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => new { t.Name, t.PickupStationId, t.DropStationId, t.Missions })
            .ToListAsync(cancellationToken);
        foreach (var c in candidates)
        {
            var routes = templates
                .Where(t => t.PickupStationId == c.Id || t.DropStationId == c.Id)
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (routes.Count > 0)
            {
                inUse[c.Id] = routes.Count == 1
                    ? $"active order template '{routes[0]}' routes through it"
                    : $"{routes.Count} active order templates route through it (e.g. '{routes[0]}')";
                continue;
            }
            if (c.Riot3StationId is not { } stationId) continue;
            var moves = templates
                .Where(t => t.Missions.Any(m => m.Type == MissionType.Move
                    && m.MapId == c.Riot3MapId && m.StationId == stationId))
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (moves.Count > 0)
                inUse[c.Id] = moves.Count == 1
                    ? $"active order template '{moves[0]}' MOVEs to it"
                    : $"{moves.Count} active order templates MOVE to it (e.g. '{moves[0]}')";
        }

        var trips = await _dispatch.Trips.AsNoTracking()
            .Where(t => !FinishedTrips.Contains(t.Status)
                && ((t.PickupStationId != null && ids.Contains(t.PickupStationId.Value))
                    || (t.DropStationId != null && ids.Contains(t.DropStationId.Value))))
            .Select(t => new { t.PickupStationId, t.DropStationId })
            .ToListAsync(cancellationToken);
        foreach (var id in ids.Where(i => !inUse.ContainsKey(i)))
        {
            var count = trips.Count(t => t.PickupStationId == id || t.DropStationId == id);
            if (count > 0) inUse[id] = $"{count} unfinished trip(s) go to it";
        }

        var items = await (
            from i in _orders.Items.AsNoTracking()
            join o in _orders.DeliveryOrders.AsNoTracking() on i.DeliveryOrderId equals o.Id
            where !FinishedOrders.Contains(o.Status)
                && ((i.PickupStationId != null && ids.Contains(i.PickupStationId.Value))
                    || (i.DropStationId != null && ids.Contains(i.DropStationId.Value)))
            select new { i.PickupStationId, i.DropStationId, o.OrderRef })
            .ToListAsync(cancellationToken);
        foreach (var id in ids.Where(i => !inUse.ContainsKey(i)))
        {
            var refs = items.Where(i => i.PickupStationId == id || i.DropStationId == id)
                .Select(i => i.OrderRef).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
            if (refs.Count > 0)
                inUse[id] = refs.Count == 1
                    ? $"unfinished order '{refs[0]}' uses it"
                    : $"{refs.Count} unfinished orders use it (e.g. '{refs[0]}')";
        }

        return inUse;
    }
}
