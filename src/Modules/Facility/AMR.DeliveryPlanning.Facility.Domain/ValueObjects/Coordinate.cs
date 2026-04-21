using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.ValueObjects;

public class Coordinate : ValueObject
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double? Theta { get; private set; } // Orientation in radians

    private Coordinate() { }

    public Coordinate(double x, double y, double? theta = null)
    {
        X = x;
        Y = y;
        Theta = theta;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return X;
        yield return Y;
        if (Theta.HasValue) yield return Theta.Value;
    }
}
