using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Item quantity paired with the unit the site actually calls it. <c>Uom</c> is
/// free-form text stored verbatim — no trimming, no case folding — because the
/// unit vocabulary belongs to the people raising the order, not to this codebase.
/// Nothing downstream branches on it: Planning never reads it, the OMS shipment
/// callbacks don't carry it, and every consumer only renders it. Suggestions
/// live in the UI (<c>UOM_SUGGESTIONS</c> in the frontend), so a new unit needs
/// no deploy here.
/// </summary>
public class Quantity : ValueObject
{
    public double Value { get; private set; }
    public string Uom { get; private set; } = null!;

    private Quantity() { }

    public static Quantity Create(double value, string uom)
    {
        if (value <= 0)
            throw new ArgumentException("Quantity value must be greater than zero.", nameof(value));
        // Invariant only — the 400 for a blank unit comes from the validators
        // (DraftItemDtoValidator / SubmitItemDtoValidator / the upstream
        // validator). ArgumentException here would surface as a 500, so reaching
        // this line means a validator was missed.
        if (string.IsNullOrWhiteSpace(uom))
            throw new ArgumentException("Quantity uom must not be empty.", nameof(uom));

        return new Quantity { Value = value, Uom = uom };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
        yield return Uom;
    }
}
