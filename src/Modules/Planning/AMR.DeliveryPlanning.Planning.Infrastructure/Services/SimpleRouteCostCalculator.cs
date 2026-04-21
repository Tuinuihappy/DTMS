using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

/// <summary>
/// Simple route cost calculator using Euclidean distance approximation.
/// In future phases, this will be replaced with actual graph traversal from Facility module.
/// </summary>
public class SimpleRouteCostCalculator : IRouteCostCalculator
{
    private readonly ILogger<SimpleRouteCostCalculator> _logger;

    public SimpleRouteCostCalculator(ILogger<SimpleRouteCostCalculator> logger)
    {
        _logger = logger;
    }

    public Task<double> CalculateCostAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default)
    {
        double cost = Calculate(fromStationId, toStationId);
        return Task.FromResult(cost);
    }

    public double Calculate(Guid fromStationId, Guid toStationId)
    {
        if (fromStationId == Guid.Empty)
            return 10.0;

        // Use hashcode-based pseudo-distance so TSP produces meaningful ordering
        var diff = Math.Abs(fromStationId.GetHashCode() - toStationId.GetHashCode());
        double cost = (diff % 100) + 1.0;

        _logger.LogDebug("Route cost {From} → {To}: {Cost}", fromStationId, toStationId, cost);
        return cost;
    }
}
