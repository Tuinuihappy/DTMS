using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Services;

/// <summary>
/// Per-mode dispatch contract — given a station/warehouse pair + order
/// context, dispatch the work to the appropriate external system.
///
/// AMR (Phase 3c) → wraps the existing OrderTemplate resolution + RIOT3
/// POST + Trip creation. Manual (Phase 4) → push notification to
/// operator + persists Trip with no vendor key. Fleet (Phase 5) → 3PL
/// shipment POST + Waybill record + Trip.
///
/// Group-level contract (not Trip-level) because today's AMR flow is
/// vendor-first: the Trip Id is the OUTCOME, not the input. A Trip-first
/// contract would force the consumer to mint a placeholder Trip with an
/// idempotency token, then patch it after vendor accept — a refactor we
/// defer until Phase 4 picks a Manual flow that genuinely benefits from
/// trip-first lifecycle.
/// </summary>
public interface IDispatchStrategy
{
    /// <summary>Mode this strategy handles. The registry indexes by this.</summary>
    TransportMode Mode { get; }

    /// <summary>
    /// Decide how the order's items split into trip-eligible groups.
    /// AMR groups by (PickupStationId, DropStationId); Manual groups
    /// by (PickupWarehouseId, DropWarehouseId); Fleet (future) may
    /// further split by carrier capacity. Returning N groups results
    /// in N dispatch calls → N trips, with items in the same group
    /// consolidated onto one trip.
    /// </summary>
    IReadOnlyList<DispatchGroup> GroupItems(IReadOnlyList<DispatchGroupItem> items);

    /// <summary>
    /// Dispatch one station-group to the external system. Returns a
    /// structured result so the caller (Planning consumer) can distinguish
    /// vendor rejected vs. persistence failed vs. data invalid and mark
    /// Job + Items accordingly.
    /// </summary>
    Task<Result<DispatchGroupOutcome>> DispatchGroupAsync(
        DispatchGroupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Primitive projection of an order item that the strategy uses to
/// decide grouping. Carries only the Ids strategies key off + the
/// ItemId so the consumer can correlate back. Defined here (rather
/// than reusing DeliveryOrder.IntegrationEvents.ItemSummaryDto) to
/// keep Dispatch.Application free of that project reference.
/// </summary>
public sealed record DispatchGroupItem(
    string ItemId,
    Guid? PickupStationId,
    Guid? DropStationId,
    // WMS PR-3 — Manual/Fleet strategies group + dispatch by WMS location.
    // Nullable so AMR items (station-based) don't need to populate them.
    Guid? PickupWmsLocationId = null,
    Guid? DropWmsLocationId = null);

/// <summary>
/// Result of <see cref="IDispatchStrategy.GroupItems"/>. Carries one
/// pair (station for AMR OR WMS location for Manual/Fleet, never both
/// populated in practice) and the subset of items in this group.
/// Consumer translates this into one <see cref="DispatchGroupRequest"/>
/// per group.
/// </summary>
public sealed record DispatchGroup(
    Guid? PickupStationId,
    Guid? DropStationId,
    IReadOnlyList<DispatchGroupItem> Items,
    Guid? PickupWmsLocationId = null,
    Guid? DropWmsLocationId = null);

/// <summary>
/// Inputs the consumer passes to <see cref="IDispatchStrategy.DispatchGroupAsync"/>.
/// AMR uses every field; Manual / Fleet ignore station Ids (they'll
/// re-resolve via the order's warehouse Ids when Phase 4 / 5 fill in
/// the bodies) and the appoint-vehicle overrides (Manual = operator-pick,
/// Fleet = provider-pick).
/// </summary>
public sealed record DispatchGroupRequest(
    Guid DeliveryOrderId,
    int GroupIndex,                       // 1-based
    Guid PickupStationId,                 // AMR uses; Manual / Fleet ignore
    Guid DropStationId,
    string UpperKey,                      // idempotency token; correlates webhooks back to the trip
    Guid? JobId,                          // 1:1 Planning anchor (b8); null when anchor failed
    int AttemptNumber = 1,
    Guid? PreviousAttemptId = null,
    int? PriorityOverride = null,
    string? AppointVehicleKeyOverride = null,
    DateTime? SlaDeadline = null,
    // WMS PR-3 — Manual/Fleet dispatch groups pass the WMS location Ids
    // resolved during order validation. Strategy uses these to look up
    // the parent zone code + geofence coords.
    Guid? PickupWmsLocationId = null,
    Guid? DropWmsLocationId = null,
    // Self-managed dispatch only — the order's RequestedBy, used as the actor
    // on the auto acknowledge + pickup. Null for AMR/Manual (they ignore it).
    string? RequestedBy = null);

/// <summary>
/// Result of a dispatch attempt. <see cref="VendorOrderKey"/> is the
/// external id (RIOT3 orderKey for AMR, null for Manual which has no
/// minted key, waybill number for Fleet). <see cref="TripId"/> is
/// <c>Guid.Empty</c> for the orphan case — vendor accepted but Trip
/// persistence failed, ops needs reconciliation.
/// </summary>
public sealed record DispatchGroupOutcome(
    Guid TripId,
    string? VendorOrderKey,
    string? TemplateName);
