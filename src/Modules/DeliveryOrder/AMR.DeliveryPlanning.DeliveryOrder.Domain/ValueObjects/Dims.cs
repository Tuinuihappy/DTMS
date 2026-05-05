using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

public sealed class Dims : ValueObject
{
    public double LengthMm { get; private set; }
    public double WidthMm { get; private set; }
    public double HeightMm { get; private set; }

    private Dims() { } // For EF Core

    public Dims(double lengthMm, double widthMm, double heightMm)
    {
        LengthMm = lengthMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return LengthMm;
        yield return WidthMm;
        yield return HeightMm;
    }
}
