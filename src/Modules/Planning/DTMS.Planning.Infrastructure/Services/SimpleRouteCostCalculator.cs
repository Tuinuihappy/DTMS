using DTMS.Facility.Application.Services;
using DTMS.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Infrastructure.Services;

public class SimpleRouteCostCalculator : IRouteCostCalculator
{
    private readonly IFacilityReadService _facilityReadService;
    private readonly ILogger<SimpleRouteCostCalculator> _logger;

    public SimpleRouteCostCalculator(IFacilityReadService facilityReadService, ILogger<SimpleRouteCostCalculator> logger)
    {
        _facilityReadService = facilityReadService;
        _logger = logger;
    }

    public async Task<double> CalculateCostAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default)
    {
        if (fromStationId == Guid.Empty || toStationId == Guid.Empty)
            return 999.0;

        var cost = await _facilityReadService.GetRouteCostAsync(
            fromStationId,
            toStationId,
            cancellationToken);

        if (!cost.HasValue)
        {
            // Expected state, not an error: RIOT3 offers no station-to-station
            // cost API (RouteEdgeSyncService was deleted 2026-07-17 for calling
            // a per-robot endpoint by mistake), and routing/distance is RIOT3's
            // responsibility. facility.RouteEdges is only ever populated
            // manually; until someone does, every pair costs the same constant
            // — fine for the legacy planning endpoints that call this, and the
            // live envelope dispatch path never asks.
            _logger.LogDebug(
                "No RouteEdge for {From}→{To} — uniform fallback cost 999.0 (RIOT3 owns real routing).",
                fromStationId, toStationId);
            return 999.0;
        }

        _logger.LogDebug("Route cost {From} → {To}: {Cost}", fromStationId, toStationId, cost.Value);
        return cost.Value;
    }

    // Sync path used by TSP solver — blocks on the async query (safe in ASP.NET Core, no SynchronizationContext)
    public double Calculate(Guid fromStationId, Guid toStationId)
        => CalculateCostAsync(fromStationId, toStationId).GetAwaiter().GetResult();
}
