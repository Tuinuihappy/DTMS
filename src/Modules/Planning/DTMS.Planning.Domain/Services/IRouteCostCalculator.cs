namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public interface IRouteCostCalculator
{
    Task<double> CalculateCostAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous cost calculation for use in TSP solvers.
    /// </summary>
    double Calculate(Guid fromStationId, Guid toStationId);
}
