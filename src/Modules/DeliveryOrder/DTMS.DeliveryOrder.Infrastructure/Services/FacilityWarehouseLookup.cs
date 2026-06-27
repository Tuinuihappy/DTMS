using DTMS.DeliveryOrder.Application.Services;
using DTMS.Facility.Application.Services;
using DoWarehouseLookupResult = DTMS.DeliveryOrder.Application.Services.WarehouseLookupResult;
using FacilityWarehouseLookupResult = DTMS.Facility.Application.Services.WarehouseLookupResult;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

/// <summary>
/// Adapter that satisfies <see cref="IWarehouseLookup"/> (DeliveryOrder.Application
/// contract) by delegating to <see cref="IFacilityReadService"/>.
///
/// Translation layer exists because the two modules have their own
/// WarehouseLookupResult record with identical shape but different
/// namespaces — keeps Application layers self-contained, mirrors the
/// existing FacilityStationLookup → StationLookupResult pattern.
/// </summary>
public class FacilityWarehouseLookup : IWarehouseLookup
{
    private readonly IFacilityReadService _facilityReadService;

    public FacilityWarehouseLookup(IFacilityReadService facilityReadService)
        => _facilityReadService = facilityReadService;

    public Task<bool> ExistsAsync(Guid warehouseId, CancellationToken ct = default)
        => _facilityReadService.WarehouseExistsAsync(warehouseId, ct);

    public Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default)
        => _facilityReadService.ResolveWarehouseByCodeAsync(code, ct);

    public async Task<IReadOnlyDictionary<string, DoWarehouseLookupResult>> ResolveBatchAsync(
        IReadOnlyList<string> warehouseCodes,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, FacilityWarehouseLookupResult> facilityResult =
            await _facilityReadService.ResolveWarehousesBatchAsync(warehouseCodes, ct);

        var translated = new Dictionary<string, DoWarehouseLookupResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, r) in facilityResult)
            translated[code] = new DoWarehouseLookupResult(r.Id, r.Code, r.Name, r.IsActive);
        return translated;
    }
}
