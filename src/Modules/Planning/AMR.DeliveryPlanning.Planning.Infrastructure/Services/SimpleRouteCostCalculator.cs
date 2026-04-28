using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class SimpleRouteCostCalculator : IRouteCostCalculator
{
    private readonly FacilityDbContext _facilityDb;
    private readonly ILogger<SimpleRouteCostCalculator> _logger;

    public SimpleRouteCostCalculator(FacilityDbContext facilityDb, ILogger<SimpleRouteCostCalculator> logger)
    {
        _facilityDb = facilityDb;
        _logger = logger;
    }

    public async Task<double> CalculateCostAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default)
    {
        if (fromStationId == Guid.Empty || toStationId == Guid.Empty)
            return 999.0;

        var edge = await _facilityDb.RouteEdges.FirstOrDefaultAsync(e =>
            (e.SourceStationId == fromStationId && e.TargetStationId == toStationId) ||
            (e.IsBidirectional && e.SourceStationId == toStationId && e.TargetStationId == fromStationId),
            cancellationToken);

        if (edge == null)
        {
            _logger.LogWarning(
                "No RouteEdge found for {From}→{To}. Returning fallback cost 999.0 — " +
                "vehicle assignment and TSP results will be incorrect. " +
                "Either populate facility.RouteEdges manually or set VendorRef on Maps/Stations " +
                "to enable RouteEdgeSyncService auto-sync from RIOT3.",
                fromStationId, toStationId);
            return 999.0;
        }

        _logger.LogDebug("Route cost {From} → {To}: {Cost} (edge={EdgeId})",
            fromStationId, toStationId, edge.Cost, edge.Id);
        return edge.Cost;
    }

    // Sync path used by TSP solver — blocks on the async query (safe in ASP.NET Core, no SynchronizationContext)
    public double Calculate(Guid fromStationId, Guid toStationId)
        => CalculateCostAsync(fromStationId, toStationId).GetAwaiter().GetResult();
}
