using System.Text.RegularExpressions;

namespace AMR.DeliveryPlanning.SharedKernel;

/// <summary>
/// Composite correlation key sent to RIOT3 as <c>upperKey</c> when DTMS
/// dispatches via OrderTemplate envelope. Encodes the parent DeliveryOrder
/// plus the station-pair group index so multi-group orders generate
/// unique RIOT3 orders while remaining traceable back to the order.
/// Attempt suffix is appended for retried trips (attempt &gt; 1) so each
/// retry produces a fresh RIOT3 order without collision.
///
/// Format:
///   First attempt:  <c>{orderId:N}-G{group}</c>
///                    e.g. <c>48752c3e35bb4d0db227cbde6c1da95b-G1</c>
///   Retry attempt:  <c>{orderId:N}-G{group}-A{attempt}</c>
///                    e.g. <c>48752c3e35bb4d0db227cbde6c1da95b-G1-A2</c>
///
/// Backward compatibility: keys persisted before the retry feature
/// (no <c>-A{n}</c> suffix) parse as attempt = 1. Build() omits the
/// suffix when attempt = 1 so RIOT3 sees an unchanged shape until the
/// first real retry.
/// </summary>
public static class EnvelopeUpperKey
{
    // Group index is 1-based on the wire (matches the operator-facing log line
    // "Group 1", "Group 2", ...). Attempt is 1-based; absent → 1.
    private static readonly Regex Pattern = new(
        @"^(?<order>[0-9a-fA-F]{32})-G(?<group>\d+)(?:-A(?<attempt>\d+))?$",
        RegexOptions.Compiled);

    public static string Build(Guid deliveryOrderId, int groupIndex, int attemptNumber = 1)
    {
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "attemptNumber must be >= 1");

        // First attempt keeps the historical shape so existing RIOT3 / DTMS
        // rows continue to round-trip identically.
        return attemptNumber == 1
            ? $"{deliveryOrderId:N}-G{groupIndex}"
            : $"{deliveryOrderId:N}-G{groupIndex}-A{attemptNumber}";
    }

    public static bool TryParse(string? upperKey, out Guid deliveryOrderId, out int groupIndex, out int attemptNumber)
    {
        deliveryOrderId = Guid.Empty;
        groupIndex = 0;
        attemptNumber = 1;

        if (string.IsNullOrWhiteSpace(upperKey)) return false;

        var match = Pattern.Match(upperKey.Trim());
        if (!match.Success) return false;

        if (!Guid.TryParseExact(match.Groups["order"].Value, "N", out deliveryOrderId)) return false;
        if (!int.TryParse(match.Groups["group"].Value, out groupIndex)) return false;

        // Attempt is optional — absent capture group means a first attempt.
        var attemptGroup = match.Groups["attempt"];
        if (attemptGroup.Success && !int.TryParse(attemptGroup.Value, out attemptNumber))
            return false;
        if (attemptNumber < 1) attemptNumber = 1;

        return true;
    }

    /// <summary>
    /// Legacy 2-out overload kept so existing callers (webhook + reconciler)
    /// compile unchanged. Attempt is silently discarded — callers that need
    /// it should use the 3-out overload.
    /// </summary>
    public static bool TryParse(string? upperKey, out Guid deliveryOrderId, out int groupIndex)
        => TryParse(upperKey, out deliveryOrderId, out groupIndex, out _);
}
