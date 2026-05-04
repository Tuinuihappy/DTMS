using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

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
            _logger.LogWarning(
                "No RouteEdge found for {From}→{To}. Returning fallback cost 999.0 — " +
                "vehicle assignment and TSP results will be incorrect. " +
                "Either populate facility.RouteEdges manually or set VendorRef on Maps/Stations " +
                "to enable RouteEdgeSyncService auto-sync from RIOT3.",
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
