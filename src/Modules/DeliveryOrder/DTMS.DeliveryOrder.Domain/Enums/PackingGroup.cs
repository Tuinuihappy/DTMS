namespace DTMS.DeliveryOrder.Domain.Enums;

/// <summary>
/// UN dangerous-goods Packing Group — degree of hazard within a hazard class.
/// Classes 1 (explosives), 2 (gases), and 7 (radioactive) do not use packing
/// groups in the UN regulations, so PackingGroup remains nullable on
/// <see cref="ValueObjects.HazmatInfo"/>.
/// </summary>
public enum PackingGroup
{
    /// <summary>Great danger.</summary>
    I,
    /// <summary>Medium danger.</summary>
    II,
    /// <summary>Minor danger.</summary>
    III
}
