using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public interface IStationValidationService
{
    Task<Result<IReadOnlyDictionary<LocationRef, Guid>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default);
}
