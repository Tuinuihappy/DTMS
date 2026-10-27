using System.Globalization;
using DTMS.Facility.Application.Services;
using DTMS.Planning.Application.Services;

namespace DTMS.Api.Adapters;

// Composition-root bridge: Planning asks whether a template's MOVE targets
// still exist in RIOT3; Facility answers from its station sync.
internal sealed class FacilityVendorStationAvailability : IVendorStationAvailability
{
    private readonly IFacilityReadService _facility;

    public FacilityVendorStationAvailability(IFacilityReadService facility) => _facility = facility;

    public async Task<IReadOnlyList<RemovedVendorStation>> FindRemovedAsync(
        IReadOnlyCollection<VendorStation> stations, CancellationToken cancellationToken = default)
    {
        if (stations.Count == 0) return [];

        var refs = stations
            .Select(s => new VendorStationRef(
                s.MapId.ToString(CultureInfo.InvariantCulture),
                s.StationId.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        var unavailable = await _facility.FindUnavailableVendorStationsAsync(refs, cancellationToken);
        return unavailable
            .Select(u => new RemovedVendorStation(
                int.Parse(u.MapVendorRef, CultureInfo.InvariantCulture),
                int.Parse(u.StationVendorRef, CultureInfo.InvariantCulture),
                u.Name))
            .ToList();
    }
}
