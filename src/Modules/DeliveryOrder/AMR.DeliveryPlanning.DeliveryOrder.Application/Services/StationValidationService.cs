using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class StationValidationService : IStationValidationService
{
    private readonly IStationLookup _stationLookup;

    public StationValidationService(IStationLookup stationLookup) => _stationLookup = stationLookup;

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
}
