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
        // MVP: return a fixed cost. In future, query the Facility module's RouteEdge graph.
        // If fromStationId is Empty (vehicle's current position unknown), use a default cost.
        double cost = fromStationId == Guid.Empty ? 10.0 : 15.0;

        _logger.LogInformation("Calculated route cost from {From} to {To}: {Cost}",
            fromStationId, toStationId, cost);

        return Task.FromResult(cost);
    }
}
