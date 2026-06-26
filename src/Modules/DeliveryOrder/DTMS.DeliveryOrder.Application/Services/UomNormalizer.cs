using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class UomNormalizer : IUomNormalizer
{
    private readonly Dictionary<string, UnitOfMeasure> _lookup;

    public UomNormalizer(IOptions<UomOptions> options)
    {
        _lookup = new Dictionary<string, UnitOfMeasure>(StringComparer.OrdinalIgnoreCase);

        // Always accept the canonical enum names so a config typo doesn't lock
        // out the most basic input form (e.g. "EA" or "KG" verbatim).
        foreach (var value in Enum.GetValues<UnitOfMeasure>())
            _lookup[value.ToString()] = value;

        foreach (var (canonical, aliases) in options.Value.Aliases)
        {
            if (!Enum.TryParse<UnitOfMeasure>(canonical, ignoreCase: true, out var enumValue))
                continue; // skip rows in config whose key isn't a known UOM

            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                _lookup[alias.Trim()] = enumValue;
            }
        }
    }

    public UnitOfMeasure? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return _lookup.TryGetValue(input.Trim(), out var uom) ? uom : null;
    }
}
