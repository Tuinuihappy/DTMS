using DTMS.DeliveryOrder.Application.Services;
using DTMS.Facility.Application.Services;
using DoStationLookupResult = DTMS.DeliveryOrder.Application.Services.StationLookupResult;
using FacilityStationLookupResult = DTMS.Facility.Application.Services.StationLookupResult;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

public class FacilityStationLookup : IStationLookup
{
    private readonly IFacilityReadService _facilityReadService;

    public FacilityStationLookup(IFacilityReadService facilityReadService)
        => _facilityReadService = facilityReadService;

    public Task<bool> ExistsAsync(Guid stationId, CancellationToken ct = default)
        => _facilityReadService.StationExistsAsync(stationId, ct);

    public Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default)
        => _facilityReadService.ResolveStationByCodeAsync(code, ct);

    public async Task<IReadOnlyDictionary<string, DoStationLookupResult>> ResolveBatchAsync(
        IReadOnlyList<string> locationCodes, CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, FacilityStationLookupResult> facilityResult =
            await _facilityReadService.ResolveStationsBatchAsync(locationCodes, ct);

        var translated = new Dictionary<string, DoStationLookupResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, r) in facilityResult)
            translated[code] = new DoStationLookupResult(r.Id, r.Code, r.IsActive, r.ManualOverrideActive, r.ManualOverrideReason);
        return translated;
    }
}
