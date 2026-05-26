using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Item quantity bound to a canonical <see cref="UnitOfMeasure"/>. The
/// previous shape — a loose <c>double Quantity</c> + <c>string Uom</c> pair —
/// allowed values like "PCS", "pcs", "moo", and "" to coexist in the DB.
/// Encapsulating both as a value object means Planning aggregation,
/// capacity checks, and reporting can rely on Uom being exhaustive.
/// </summary>
public class Quantity : ValueObject
{
    public double Value { get; private set; }
    public UnitOfMeasure Uom { get; private set; }

    private Quantity() { }

    public static Quantity Create(double value, UnitOfMeasure uom)
    {
        if (value <= 0)
            throw new ArgumentException("Quantity value must be greater than zero.", nameof(value));

        return new Quantity { Value = value, Uom = uom };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
        yield return Uom;
    }
}
