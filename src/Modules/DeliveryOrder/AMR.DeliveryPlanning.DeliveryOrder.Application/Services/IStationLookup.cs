namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public interface IStationLookup
{
    Task<bool> ExistsAsync(Guid stationId, CancellationToken ct = default);
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default);
}
