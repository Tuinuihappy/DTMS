using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Temperature range (°C) that an item must stay within during transport.
/// Either bound may be null (open on that side), but at least one must be
/// set. When both are set, MinC must be on or below MaxC. Items without a
/// TemperatureRange are ambient — no constraint. Extreme values (e.g.
/// cryogenic at -196°C or industrial at 300°C) are accepted because
/// different industries genuinely need them; sanity bounds belong in
/// the validator tier (P2-8) if needed at all.
/// </summary>
public class TemperatureRange : ValueObject
{
    public double? MinC { get; private set; }
    public double? MaxC { get; private set; }

    private TemperatureRange() { }

    public static TemperatureRange Create(double? minC, double? maxC)
    {
        if (minC is null && maxC is null)
            throw new ArgumentException("TemperatureRange must have at least one bound (MinC or MaxC).");
        if (minC.HasValue && maxC.HasValue && minC.Value > maxC.Value)
            throw new ArgumentException("TemperatureRange.MinC must be on or below MaxC.");

        return new TemperatureRange { MinC = minC, MaxC = maxC };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return MinC ?? double.NegativeInfinity;
        yield return MaxC ?? double.PositiveInfinity;
    }
}
