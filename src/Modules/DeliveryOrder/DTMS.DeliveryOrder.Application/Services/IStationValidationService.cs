using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Services;

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

    /// <summary>
    /// WMS PR-2 — Manual/Fleet transport-mode order path. Resolves each item's
    /// PickupLocationCode/DropLocationCode against the local WMS snapshot
    /// (populated by <c>WmsLocationSyncService</c>). Rejects unknown or
    /// upstream-inactive codes with a clear message so the order stays out
    /// of the Validated state pointing at a location the dispatcher couldn't
    /// route to.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, Guid>>>
        BuildWmsLocationMapAsync(IEnumerable<Item> items, CancellationToken ct = default);
}
