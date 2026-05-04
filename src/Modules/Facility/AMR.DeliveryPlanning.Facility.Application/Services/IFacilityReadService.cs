namespace AMR.DeliveryPlanning.Facility.Application.Services;

public sealed record StationVendorTarget(
    Guid StationId,
    Guid MapId,
    string MapVendorRef,
    string StationVendorRef);

public interface IFacilityReadService
{
    Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default);

    Task<StationVendorTarget?> GetStationVendorTargetAsync(
        Guid stationId,
        CancellationToken cancellationToken = default);

    Task<double?> GetRouteCostAsync(
        Guid fromStationId,
        Guid toStationId,
        CancellationToken cancellationToken = default);
}
