namespace DTMS.DeliveryOrder.Domain.Enums;

/// <summary>
/// Canonical units of measure accepted by the domain. Aliases from upstream
/// systems (SAP "KGM", manual "กก", MES "kg") are normalized into one of these
/// at the Application boundary via <c>IUomNormalizer</c>. The domain only ever
/// sees these seven values, which keeps the Planning solver's aggregation /
/// capacity logic exhaustive.
/// </summary>
public enum UnitOfMeasure
{
    // Mass
    KG,
    G,
    LB,
    // Count
    EA,
    BOX,
    PALLET,
    CASE
}
