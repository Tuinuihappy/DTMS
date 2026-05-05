using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

public sealed class TemperatureRange : ValueObject
{
    public double? MinCelsius { get; private set; }
    public double? MaxCelsius { get; private set; }

    private TemperatureRange() { } // For EF Core

    public TemperatureRange(double? minCelsius, double? maxCelsius)
    {
        MinCelsius = minCelsius;
        MaxCelsius = maxCelsius;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return (object?)MinCelsius ?? DBNull.Value;
        yield return (object?)MaxCelsius ?? DBNull.Value;
    }
}
