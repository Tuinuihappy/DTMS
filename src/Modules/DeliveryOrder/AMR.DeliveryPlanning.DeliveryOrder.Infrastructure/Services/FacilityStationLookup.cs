using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.Facility.Application.Services;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;

public class FacilityStationLookup : IStationLookup
{
    private readonly IFacilityReadService _facilityReadService;

    public FacilityStationLookup(IFacilityReadService facilityReadService)
        => _facilityReadService = facilityReadService;

    public Task<bool> ExistsAsync(Guid stationId, CancellationToken ct = default)
        => _facilityReadService.StationExistsAsync(stationId, ct);
}
