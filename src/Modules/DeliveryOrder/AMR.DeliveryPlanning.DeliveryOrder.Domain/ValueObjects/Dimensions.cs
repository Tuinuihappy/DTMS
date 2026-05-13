using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

public class Dimensions : ValueObject
{
    public double LengthCm { get; private set; }
    public double WidthCm { get; private set; }
    public double HeightCm { get; private set; }

    private Dimensions() { }

    public static Dimensions Create(double lengthCm, double widthCm, double heightCm)
    {
        if (lengthCm <= 0 || widthCm <= 0 || heightCm <= 0)
            throw new ArgumentException("All dimensions must be greater than zero.");

        return new Dimensions { LengthCm = lengthCm, WidthCm = widthCm, HeightCm = heightCm };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return LengthCm;
        yield return WidthCm;
        yield return HeightCm;
    }
}
