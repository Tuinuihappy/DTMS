using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Services;

public class StationValidationService : IStationValidationService
{
    private readonly IStationLookup _stationLookup;
    private readonly IWmsLocationLookup _wmsLocationLookup;

    public StationValidationService(
        IStationLookup stationLookup,
        IWmsLocationLookup wmsLocationLookup)
    {
        _stationLookup = stationLookup;
        _wmsLocationLookup = wmsLocationLookup;
    }

    /// <summary>
    /// Reads back which check a failure message from this service came from,
    /// and the code it names — for the intake metrics' reason/location labels.
    /// Lives beside the messages it parses; StationValidationClassifyTests pins
    /// every message shape so a reworded one can't silently fall to "invalid".
    /// </summary>
    public static (string Reason, string? Code) ClassifyFailure(string error)
    {
        var code = error.StartsWith('\'') && error.IndexOf('\'', 1) is > 0 and var end
            ? error[1..end]
            : null;
        var reason =
            error.EndsWith("is not a valid station code.", StringComparison.Ordinal) ? Diagnostics.OrderIntakeMetrics.Reasons.StationUnknown
            : error.Contains("is deactivated and cannot be used", StringComparison.Ordinal) ? Diagnostics.OrderIntakeMetrics.Reasons.StationInactive
            : error.Contains("is currently offline", StringComparison.Ordinal) ? Diagnostics.OrderIntakeMetrics.Reasons.StationOffline
            : error.EndsWith("is not a valid WMS location.", StringComparison.Ordinal) ? Diagnostics.OrderIntakeMetrics.Reasons.WmsLocationUnknown
            : error.Contains("is no longer active in WMS", StringComparison.Ordinal) ? Diagnostics.OrderIntakeMetrics.Reasons.WmsLocationInactive
            : Diagnostics.OrderIntakeMetrics.Reasons.Invalid;
        return (reason, reason == Diagnostics.OrderIntakeMetrics.Reasons.Invalid ? null : code);
    }

    // AMR path (existing) — interpret location codes as station codes.
    // The order's PickupLocationCode / DropLocationCode resolve to AMR
    // station Ids. Inactive / manually-overridden stations are rejected
    // here so the order can't get into the Validated state pointing at
    // a station that the dispatcher would refuse to dispatch to.
    public async Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        var codes = items
            .SelectMany(i => new[] { i.PickupLocationCode, i.DropLocationCode })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = await _stationLookup.ResolveBatchAsync(codes, ct);

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            if (!resolved.TryGetValue(code, out var station))
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    $"'{code}' is not a valid station code.");

            if (!station.IsActive)
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    $"'{code}' is deactivated and cannot be used for new orders.");

            if (station.ManualOverrideActive)
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    string.IsNullOrWhiteSpace(station.ManualOverrideReason)
                        ? $"'{code}' is currently offline (manual override)."
                        : $"'{code}' is currently offline (manual override): {station.ManualOverrideReason}");

            map[code] = station.Id;
        }

        return Result<IReadOnlyDictionary<string, Guid>>.Success(map);
    }

    // Manual path (Phase 2.5 Path A) — interpret location codes
    // as warehouse codes. Mirror of the station path: dedupe + batch
    // lookup + reject inactive entries with a clear "not found" /
    // "deactivated" message instead of letting the order land in a
    // half-validated state.
    //
    // No manual-override concept on Warehouse yet (that's a Station
    // operator-driven flag); when warehouses gain a similar lifecycle
    // hook in a later phase we add the equivalent check here.
    // WMS PR-2 path — resolve every unique location code in the order
    // against the local WMS snapshot. Mirrors the station/warehouse
    // shape so the caller's control flow doesn't have to change beyond
    // picking the right method by RequestedTransportMode.
    public async Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildWmsLocationMapAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        var codes = items
            .SelectMany(i => new[] { i.PickupLocationCode, i.DropLocationCode })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = await _wmsLocationLookup.ResolveBatchAsync(codes, ct);

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            if (!resolved.TryGetValue(code, out var loc))
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    $"'{code}' is not a valid WMS location.");

            if (!loc.IsActive)
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    $"'{code}' is no longer active in WMS and cannot be used for new orders.");

            map[code] = loc.Id;
        }

        return Result<IReadOnlyDictionary<string, Guid>>.Success(map);
    }
}
