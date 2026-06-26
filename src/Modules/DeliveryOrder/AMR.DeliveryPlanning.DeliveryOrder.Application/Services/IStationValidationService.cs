using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

/// <summary>
/// Builds the (location-code → resolved-id) maps that
/// <see cref="DeliveryOrder.MarkAsValidated"/> consumes during order
/// submission. Despite the name (kept stable to avoid a cross-module
/// rename) this now handles BOTH station and warehouse lookups —
/// callers pick the right method based on the order's
/// <see cref="RequestedTransportMode"/>:
///
///   - <see cref="BuildStationMapAsync"/> for AMR orders (location
///     code = station code; existing behaviour).
///   - <see cref="BuildWarehouseMapAsync"/> for Manual / Fleet orders
///     (location code = warehouse code; Phase 2.5 Path A — wires
///     IWarehouseLookup into the validation pipeline).
/// </summary>
public interface IStationValidationService
{
    Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildStationMapAsync(IEnumerable<Item> items, CancellationToken ct = default);

    Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildWarehouseMapAsync(IEnumerable<Item> items, CancellationToken ct = default);
}
