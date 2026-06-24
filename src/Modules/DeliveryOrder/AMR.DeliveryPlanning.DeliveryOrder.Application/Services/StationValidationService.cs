using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class StationValidationService : IStationValidationService
{
    private readonly IStationLookup _stationLookup;
    private readonly IWarehouseLookup _warehouseLookup;

    public StationValidationService(
        IStationLookup stationLookup,
        IWarehouseLookup warehouseLookup)
    {
        _stationLookup = stationLookup;
        _warehouseLookup = warehouseLookup;
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

    // Manual / Fleet path (Phase 2.5 Path A) — interpret location codes
    // as warehouse codes. Mirror of the station path: dedupe + batch
    // lookup + reject inactive entries with a clear "not found" /
    // "deactivated" message instead of letting the order land in a
    // half-validated state.
    //
    // No manual-override concept on Warehouse yet (that's a Station
    // operator-driven flag); when warehouses gain a similar lifecycle
    // hook in a later phase we add the equivalent check here.
    public async Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildWarehouseMapAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        var codes = items
            .SelectMany(i => new[] { i.PickupLocationCode, i.DropLocationCode })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = await _warehouseLookup.ResolveBatchAsync(codes, ct);

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            if (!resolved.TryGetValue(code, out var warehouse))
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    $"'{code}' is not a valid warehouse code.");

            if (!warehouse.IsActive)
                return Result<IReadOnlyDictionary<string, Guid>>.Failure(
                    $"'{code}' is deactivated and cannot be used for new orders.");

            map[code] = warehouse.Id;
        }

        return Result<IReadOnlyDictionary<string, Guid>>.Success(map);
    }
}
