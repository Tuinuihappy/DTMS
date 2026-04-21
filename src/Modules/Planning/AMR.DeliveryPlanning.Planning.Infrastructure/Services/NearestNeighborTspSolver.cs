using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

/// <summary>
/// Nearest-Neighbor TSP heuristic for Multi-Stop route optimization.
/// Greedily visits the closest unvisited station at each step.
/// Time complexity: O(n²) — suitable for small-to-medium drop counts.
/// </summary>
public class NearestNeighborTspSolver : IRouteSolver
{
    private readonly IRouteCostCalculator _costCalculator;
    private readonly ILogger<NearestNeighborTspSolver> _logger;

    public NearestNeighborTspSolver(IRouteCostCalculator costCalculator, ILogger<NearestNeighborTspSolver> logger)
    {
        _costCalculator = costCalculator;
        _logger = logger;
    }

    public List<Guid> SolveRoute(Guid startStation, List<Guid> dropStations)
    {
        if (dropStations.Count <= 1)
            return new List<Guid>(dropStations);

        var unvisited = new List<Guid>(dropStations);
        var route = new List<Guid>();
        var current = startStation;
        double totalCost = 0;

        while (unvisited.Count > 0)
        {
            // Find nearest unvisited station
            Guid nearest = unvisited[0];
            double nearestCost = _costCalculator.Calculate(current, nearest);

            for (int i = 1; i < unvisited.Count; i++)
            {
                double cost = _costCalculator.Calculate(current, unvisited[i]);
                if (cost < nearestCost)
                {
                    nearest = unvisited[i];
                    nearestCost = cost;
                }
            }

            route.Add(nearest);
            totalCost += nearestCost;
            current = nearest;
            unvisited.Remove(nearest);
        }

        _logger.LogInformation("TSP solved: {Count} stops, total cost={Cost:F1}", route.Count, totalCost);
        return route;
    }
}
