using System.Text.RegularExpressions;

namespace AMR.DeliveryPlanning.SharedKernel;

/// <summary>
/// Composite correlation key sent to RIOT3 as <c>upperKey</c> when DTMS
/// dispatches via OrderTemplate envelope. Encodes the parent DeliveryOrder
/// plus the station-pair group index so multi-group orders generate
/// unique RIOT3 orders while remaining traceable back to the order.
///
/// Format: <c>{deliveryOrderId:N}-G{groupIndex}</c> — e.g.
/// <c>48752c3e35bb4d0db227cbde6c1da95b-G1</c>.
/// </summary>
public static class EnvelopeUpperKey
{
    // Group index is 1-based on the wire (matches the operator-facing log line
    // "Group 1", "Group 2", ...) but stored 0-based internally is fine since
    // the parser exposes whatever the caller wrote.
    private static readonly Regex Pattern = new(
        @"^(?<order>[0-9a-fA-F]{32})-G(?<group>\d+)$",
        RegexOptions.Compiled);

    public static string Build(Guid deliveryOrderId, int groupIndex)
        => $"{deliveryOrderId:N}-G{groupIndex}";

    public static bool TryParse(string? upperKey, out Guid deliveryOrderId, out int groupIndex)
    {
        deliveryOrderId = Guid.Empty;
        groupIndex = 0;

        if (string.IsNullOrWhiteSpace(upperKey)) return false;

        var match = Pattern.Match(upperKey.Trim());
        if (!match.Success) return false;

        if (!Guid.TryParseExact(match.Groups["order"].Value, "N", out deliveryOrderId)) return false;
        if (!int.TryParse(match.Groups["group"].Value, out groupIndex)) return false;
        return true;
    }
}
