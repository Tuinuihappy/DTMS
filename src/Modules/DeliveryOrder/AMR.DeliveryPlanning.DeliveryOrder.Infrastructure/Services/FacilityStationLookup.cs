using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;

public class FacilityStationLookup : IStationLookup
{
    private readonly FacilityDbContext _facilityDb;

    public FacilityStationLookup(FacilityDbContext facilityDb)
        => _facilityDb = facilityDb;

    public Task<bool> ExistsAsync(Guid stationId, CancellationToken ct = default)
        => _facilityDb.Stations.AnyAsync(s => s.Id == stationId, ct);
}
