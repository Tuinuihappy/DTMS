using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

public class Dimensions : ValueObject
{
    public double LengthMm { get; private set; }
    public double WidthMm { get; private set; }
    public double HeightMm { get; private set; }

    public double VolumeCBM => LengthMm * WidthMm * HeightMm / 1_000_000_000.0;

    private Dimensions() { }

    public static Dimensions Create(double lengthMm, double widthMm, double heightMm)
    {
        if (lengthMm <= 0 || widthMm <= 0 || heightMm <= 0)
            throw new ArgumentException("All dimensions must be greater than zero.");

        return new Dimensions { LengthMm = lengthMm, WidthMm = widthMm, HeightMm = heightMm };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return LengthMm;
        yield return WidthMm;
        yield return HeightMm;
    }
}
