using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public interface IStationValidationService
{
    Task<Result<IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default);
}
