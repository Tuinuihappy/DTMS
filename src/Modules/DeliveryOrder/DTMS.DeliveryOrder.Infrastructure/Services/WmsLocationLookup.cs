using DTMS.DeliveryOrder.Application.Services;
using DTMS.Wms.Domain.Repositories;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

/// <summary>
/// Adapter that satisfies <see cref="IWmsLocationLookup"/>
/// (DeliveryOrder.Application contract) by delegating to
/// <see cref="IWmsLocationRepository"/> (WMS module).
///
/// Two-module contract exists so the DeliveryOrder application layer
/// doesn't take a direct dependency on the WMS module's public API —
/// same pattern as <see cref="FacilityWarehouseLookup"/>. If WMS ever
/// gets split into its own service the swap is one file.
/// </summary>
public class WmsLocationLookup : IWmsLocationLookup
{
    private readonly IWmsLocationRepository _repo;

    public WmsLocationLookup(IWmsLocationRepository repo)
    {
        _repo = repo;
    }

    public Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default)
        => _repo.ResolveByCodeAsync(code, ct);

    public async Task<WmsLocationLookupResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var loc = await _repo.GetByIdAsync(id, ct);
        if (loc is null) return null;
        return new WmsLocationLookupResult(
            Id: loc.Id,
            ExternalId: loc.ExternalId,
            Code: loc.LocationCode,
            DisplayName: loc.DisplayName,
            IsActive: loc.IsActive,
            ParentLocationCode: loc.ParentLocationCode,
            Latitude: loc.Latitude,
            Longitude: loc.Longitude);
    }

    public async Task<IReadOnlyDictionary<string, WmsLocationLookupResult>> ResolveBatchAsync(
        IReadOnlyList<string> codes,
        CancellationToken ct = default)
    {
        var matched = await _repo.ResolveBatchAsync(codes, ct);

        var translated = new Dictionary<string, WmsLocationLookupResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (input, loc) in matched)
        {
            translated[input] = new WmsLocationLookupResult(
                Id: loc.Id,
                ExternalId: loc.ExternalId,
                Code: loc.LocationCode,
                DisplayName: loc.DisplayName,
                IsActive: loc.IsActive,
                ParentLocationCode: loc.ParentLocationCode,
                Latitude: loc.Latitude,
                Longitude: loc.Longitude);
        }
        return translated;
    }
}
