using DTMS.DeliveryOrder.Domain.Enums;

namespace DTMS.DeliveryOrder.Application.Options;

/// <summary>
/// Boundary translation table for upstream UOM strings. Keyed by canonical
/// <see cref="UnitOfMeasure"/> name; each value lists the aliases that should
/// normalize into it. The canonical name itself is always accepted regardless
/// of whether it appears in the alias list.
/// </summary>
public class UomOptions
{
    public const string SectionName = "Uom";

    /// <summary>
    /// Canonical UOM → aliases that map to it. Lookup is case-insensitive and
    /// trim-aware (see <c>UomNormalizer</c>). Adding a new alias is a config
    /// change — no redeploy needed.
    /// </summary>
    public Dictionary<string, string[]> Aliases { get; set; } = new();
}
