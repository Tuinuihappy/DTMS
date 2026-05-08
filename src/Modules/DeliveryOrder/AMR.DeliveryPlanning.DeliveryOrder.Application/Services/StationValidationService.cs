using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class StationValidationService
{
    private readonly IStationLookup _stationLookup;

    public StationValidationService(IStationLookup stationLookup) => _stationLookup = stationLookup;

    public async Task<(bool Success, Guid StationId, string? Error)> ResolveAndValidateAsync(
        string locationCode, string fieldName, CancellationToken ct = default)
    {
        if (Guid.TryParse(locationCode, out var stationId))
        {
            if (!await _stationLookup.ExistsAsync(stationId, ct))
                return (false, Guid.Empty, $"{fieldName} station '{stationId}' does not exist.");
            return (true, stationId, null);
        }

        var resolvedId = await _stationLookup.ResolveByCodeAsync(locationCode.ToUpperInvariant(), ct);
        if (resolvedId is null)
            return (false, Guid.Empty, $"{fieldName} '{locationCode}' is not a valid station ID or code.");

        return (true, resolvedId.Value, null);
    }

    public async Task<Result<IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)>>>
        BuildStationMapAsync(IEnumerable<DeliveryLeg> legs, CancellationToken ct = default)
    {
        var uniquePairs = legs
            .Select(l => (l.PickupLocationCode, l.DropLocationCode))
            .Distinct()
            .ToList();

        // Deduplicate all location codes and resolve them in parallel
        var allCodes = uniquePairs
            .SelectMany(p => new[]
            {
                (Code: p.PickupLocationCode, Field: "PickupLocationCode"),
                (Code: p.DropLocationCode,   Field: "DropLocationCode")
            })
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var results = await Task.WhenAll(
            allCodes.Select(x => ResolveAndValidateAsync(x.Code, x.Field, ct)));

        var codeToStation = new Dictionary<string, (bool Ok, Guid Id, string? Err)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < allCodes.Count; i++)
            codeToStation[allCodes[i].Code] = results[i];

        foreach (var (_, (ok, _, err)) in codeToStation)
            if (!ok) return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Failure(err!);

        var map = uniquePairs.ToDictionary(
            p => (p.PickupLocationCode, p.DropLocationCode),
            p => (codeToStation[p.PickupLocationCode].Id, codeToStation[p.DropLocationCode].Id));

        return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Success(map);
    }
}
