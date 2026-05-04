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
}
