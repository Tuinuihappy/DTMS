using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Application.Services;

namespace DTMS.DeliveryOrder.Application.Services;

/// <summary>
/// Why an order was refused for its routes: the caller-readable message, and
/// each failing route as "PICKUP → DROP" (the intake metrics' location label).
/// </summary>
public sealed record RouteRejection(string Message, IReadOnlyList<string> Routes)
{
    public RouteNotDispatchableException ToException() => new(Message);
}

/// <summary>
/// Refuses an order up front when one of its routes can't be dispatched —
/// for AMR, no active OrderTemplate for a pickup → drop station pair, or one
/// that doesn't resolve. Without it the order is accepted, planned, fails in
/// Planning seconds later and lands at Failed with nobody told why; for a
/// source system that means it holds goods for an order that will never move.
/// Every path that sends an order into planning uses it — source-system
/// intake, Submit, Bulk submit, Edit &amp; resend — so they agree on what
/// "dispatchable" means and say it the same way.
/// </summary>
public interface IRouteDispatchGuard
{
    /// <summary>
    /// Null when every route of <paramref name="items"/> is dispatchable;
    /// otherwise the rejection, naming each failing route by its location
    /// codes. Callers throw <see cref="RouteRejection.ToException"/> (422) or,
    /// in a batch, collect the message.
    /// </summary>
    Task<RouteRejection?> CheckAsync(
        string orderRef, IReadOnlyCollection<Item> items, TransportMode mode,
        IReadOnlyDictionary<string, Guid>? stationMap, CancellationToken ct = default);
}

public sealed class RouteDispatchGuard(IDispatchStrategyRegistry strategies) : IRouteDispatchGuard
{
    public async Task<RouteRejection?> CheckAsync(
        string orderRef, IReadOnlyCollection<Item> items, TransportMode mode,
        IReadOnlyDictionary<string, Guid>? stationMap, CancellationToken ct = default)
    {
        // Routes are station pairs — exactly the AMR dispatch groups. Modes
        // that route by WMS location have their codes checked on resolution
        // and nothing else to fail before dispatch.
        if (stationMap is null || items.Count == 0) return null;

        var routes = items
            .GroupBy(i => (Pickup: stationMap[i.PickupLocationCode], Drop: stationMap[i.DropLocationCode]))
            .ToDictionary(g => g.Key, g => $"{g.First().PickupLocationCode} → {g.First().DropLocationCode}");

        var problems = await strategies.Get(mode).CheckRoutesAsync(routes.Keys, ct);
        if (problems.Count == 0) return null;

        var failing = problems.Select(p => routes[(p.Pickup, p.Drop)]).ToList();
        var detail = string.Join("; ", problems.Select(p => $"{routes[(p.Pickup, p.Drop)]}: {p.Reason}"));
        return new RouteRejection(
            $"Order '{orderRef}' can't be dispatched — {detail}. " +
            "Add or activate an order template for the route, then send the order again.",
            failing);
    }
}
