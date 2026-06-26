using System.Text.RegularExpressions;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Dangerous-goods classification attached to an <see cref="Entities.Item"/>.
/// Per Decision #5 the domain tracks only the hazard <see cref="ClassCode"/>
/// (e.g. "3", "2.1", "5.1") and an optional <see cref="PackingGroup"/>;
/// the UN number is intentionally out of scope until B2B / EDI work picks it
/// up. Items without a HazmatInfo are non-hazardous — most goods in an
/// in-plant flow.
/// </summary>
public class HazmatInfo : ValueObject
{
    /// <summary>
    /// UN hazard class, optionally with a subdivision: digit 1-9, optionally
    /// followed by a dot and a digit 1-6. Subdivisions matter because e.g.
    /// 2.1 (flammable gas) and 2.3 (toxic gas) have very different
    /// segregation rules even though both are class 2.
    /// </summary>
    public string ClassCode { get; private set; } = string.Empty;

    public PackingGroup? PackingGroup { get; private set; }

    private static readonly Regex Pattern =
        new(@"^[1-9](\.[1-6])?$", RegexOptions.Compiled);

    private HazmatInfo() { }

    public static HazmatInfo Create(string classCode, PackingGroup? packingGroup)
    {
        if (string.IsNullOrWhiteSpace(classCode))
            throw new ArgumentException("HazmatInfo.ClassCode is required.", nameof(classCode));

        var trimmed = classCode.Trim();
        if (!Pattern.IsMatch(trimmed))
            throw new ArgumentException(
                $"Invalid hazard class '{classCode}'. Expected a UN class 1-9 with optional subdivision (e.g. '3', '2.1', '5.1').",
                nameof(classCode));

        return new HazmatInfo { ClassCode = trimmed, PackingGroup = packingGroup };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ClassCode;
        yield return PackingGroup ?? (object)"<none>";
    }
}
