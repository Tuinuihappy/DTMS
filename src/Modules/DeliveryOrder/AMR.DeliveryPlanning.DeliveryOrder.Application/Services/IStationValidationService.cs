using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public interface IStationValidationService
{
    Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default);
}
