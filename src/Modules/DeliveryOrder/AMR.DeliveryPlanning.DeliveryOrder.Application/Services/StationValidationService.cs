using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class StationValidationService : IStationValidationService
{
    private readonly IStationLookup _stationLookup;

    public StationValidationService(IStationLookup stationLookup) => _stationLookup = stationLookup;

    public async Task<Result<IReadOnlyDictionary<LocationRef, Guid>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        var refs = items
            .SelectMany(i => new[] { i.PickupLocation, i.DropLocation })
            .Distinct()
            .ToList();

        // Underlying lookup is string-based and handles both Code and Guid forms.
        // Stringify each ref so we can reuse the existing Facility batch endpoint
        // and the Redis cache layer in CachedStationLookup.
        var inputs = refs
            .Select(StringifyRef)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = await _stationLookup.ResolveBatchAsync(inputs, ct);

        var map = new Dictionary<LocationRef, Guid>();
        foreach (var r in refs)
        {
            var key = StringifyRef(r);

            if (!resolved.TryGetValue(key, out var station))
                return Result<IReadOnlyDictionary<LocationRef, Guid>>.Failure(
                    $"'{r}' is not a valid station ID or code.");

            if (!station.IsActive)
                return Result<IReadOnlyDictionary<LocationRef, Guid>>.Failure(
                    $"'{r}' is deactivated and cannot be used for new orders.");

            if (station.ManualOverrideActive)
                return Result<IReadOnlyDictionary<LocationRef, Guid>>.Failure(
                    string.IsNullOrWhiteSpace(station.ManualOverrideReason)
                        ? $"'{r}' is currently offline (manual override)."
                        : $"'{r}' is currently offline (manual override): {station.ManualOverrideReason}");

            map[r] = station.Id;
        }

        return Result<IReadOnlyDictionary<LocationRef, Guid>>.Success(map);
    }

    private static string StringifyRef(LocationRef r) =>
        r.IsCode ? r.Code! : r.StationId!.Value.ToString();
}
