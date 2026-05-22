using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class StationValidationService : IStationValidationService
{
    private readonly IStationLookup _stationLookup;

    public StationValidationService(IStationLookup stationLookup) => _stationLookup = stationLookup;

    public async Task<Result<IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        var uniquePairs = items
            .Select(i => (i.PickupLocationCode, i.DropLocationCode))
            .Distinct()
            .ToList();

        var allCodes = uniquePairs
            .SelectMany(p => new[] { p.PickupLocationCode, p.DropLocationCode })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = await _stationLookup.ResolveBatchAsync(allCodes, ct);

        foreach (var code in allCodes)
        {
            if (!resolved.TryGetValue(code, out var station))
                return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Failure(
                    $"'{code}' is not a valid station ID or code.");

            if (!station.IsActive)
                return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Failure(
                    $"'{code}' is deactivated and cannot be used for new orders.");

            if (station.ManualOverrideActive)
                return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Failure(
                    string.IsNullOrWhiteSpace(station.ManualOverrideReason)
                        ? $"'{code}' is currently offline (manual override)."
                        : $"'{code}' is currently offline (manual override): {station.ManualOverrideReason}");
        }

        var map = uniquePairs.ToDictionary(
            p => (p.PickupLocationCode, p.DropLocationCode),
            p => (resolved[p.PickupLocationCode].Id, resolved[p.DropLocationCode].Id));

        return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Success(map);
    }
}
