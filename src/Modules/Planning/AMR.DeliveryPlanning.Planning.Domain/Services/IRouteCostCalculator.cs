namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public interface IRouteCostCalculator
{
    Task<double> CalculateCostAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default);
}
