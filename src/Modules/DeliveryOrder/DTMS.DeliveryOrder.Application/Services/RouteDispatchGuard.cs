using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Application.Services;

namespace DTMS.DeliveryOrder.Application.Services;

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
    /// otherwise a caller-readable sentence naming each failing route by its
    /// location codes. For callers that collect failures (Bulk submit).
    /// </summary>
    Task<string?> FindProblemAsync(
        string orderRef, IReadOnlyCollection<Item> items, TransportMode mode,
        IReadOnlyDictionary<string, Guid>? stationMap, CancellationToken ct = default);

    /// <summary>Throws <see cref="RouteNotDispatchableException"/> (422) when
    /// <see cref="FindProblemAsync"/> finds one.</summary>
    Task EnsureDispatchableAsync(
        string orderRef, IReadOnlyCollection<Item> items, TransportMode mode,
        IReadOnlyDictionary<string, Guid>? stationMap, CancellationToken ct = default);
}

public sealed class RouteDispatchGuard(IDispatchStrategyRegistry strategies) : IRouteDispatchGuard
{
    public async Task<string?> FindProblemAsync(
        string orderRef, IReadOnlyCollection<Item> items, TransportMode mode,
        IReadOnlyDictionary<string, Guid>? stationMap, CancellationToken ct = default)
    {
        // Routes are station pairs — exactly the AMR dispatch groups. Modes
        // that route by WMS location have their codes checked on resolution
        // and nothing else to fail before dispatch.
        if (stationMap is null || items.Count == 0) return null;

        var routes = items
            .GroupBy(i => (Pickup: stationMap[i.PickupLocationCode], Drop: stationMap[i.DropLocationCode]))
            .ToDictionary(g => g.Key, g => (g.First().PickupLocationCode, g.First().DropLocationCode));

        var problems = await strategies.Get(mode).CheckRoutesAsync(routes.Keys, ct);
        if (problems.Count == 0) return null;

        var detail = string.Join("; ", problems.Select(p =>
        {
            var (pickup, drop) = routes[(p.Pickup, p.Drop)];
            return $"{pickup} → {drop}: {p.Reason}";
        }));
        return $"Order '{orderRef}' can't be dispatched — {detail}. " +
               "Add or activate an order template for the route, then send the order again.";
    }

    public async Task EnsureDispatchableAsync(
        string orderRef, IReadOnlyCollection<Item> items, TransportMode mode,
        IReadOnlyDictionary<string, Guid>? stationMap, CancellationToken ct = default)
    {
        if (await FindProblemAsync(orderRef, items, mode, stationMap, ct) is { } problem)
            throw new RouteNotDispatchableException(problem);
    }
}
