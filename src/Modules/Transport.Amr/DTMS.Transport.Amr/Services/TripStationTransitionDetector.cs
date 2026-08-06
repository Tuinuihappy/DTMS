using System.Globalization;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Facility.Application.Services;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Amr.Services;

/// <summary>
/// Shared vendor pickup/drop detection: when a RIOT3 sub-task/mission FINISHES
/// at the trip's pickup or drop station, fire the matching Trip signal. Lives
/// in one place so BOTH the webhook (Riot3Webhooks.HandleSubTaskEvent) and the
/// reconciler (Riot3ReconciliationService) run identical logic — the reconciler
/// is the safety net when a sub-task webhook is dropped, so pickup/drop don't
/// silently vanish (or, on a round-trip template, fire on the wrong visit).
///
/// Idempotency: Trip.MarkVendorPickedUp / MarkVendorDropCompleted are fire-once
/// (guarded by VendorPickedUpAt / VendorDroppedAt), so the two callers observing
/// the same mission — or the robot revisiting the pickup station on its way back
/// — never double-fire. This helper additionally short-circuits before the POD
/// lookup once dropped.
///
/// Ordering: drop is gated on pickup having already fired (when the trip has a
/// pickup leg) — round-trip templates visit the drop station first to fetch the
/// empty rack, and that visit must not count as the drop.
///
/// Resolves via the vendor-side station id (VendorRef) rather than station name:
/// RIOT3 emits the name in its own casing ("Station165") which won't match the
/// upper-cased Code DTMS stores, and IDs are stable across vendor renames.
/// </summary>
public static class TripStationTransitionDetector
{
    /// <summary>
    /// Applies pickup/drop detection for one finished mission. Mutates the Trip
    /// (does NOT persist — the caller owns the save) and returns true when a
    /// pickup or drop actually fired, so the caller knows to UpdateAsync.
    /// </summary>
    public static async Task<bool> TryApplyAsync(
        Trip trip,
        string? missionType,
        string? state,
        int? vendorStationId,
        IFacilityReadService facilityReadService,
        IDeliveryOrderStatusReader orderReader,
        DateTime? actedAt,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(state, "FINISHED", StringComparison.OrdinalIgnoreCase))
            return false;

        var type = string.IsNullOrWhiteSpace(missionType) ? "" : missionType.ToUpperInvariant();
        // MOVE FINISHED = robot arrived at the station — the ONLY mission type
        // whose station is trustworthy. ACT frames DO carry a station object,
        // but it is a stale "last registered dock" that lags the robot by one
        // leg (trip 5018, 2026-07-16: act…d2 carried STF_13 — the trip's drop
        // station — while the robot was actually at SHELF3_STANBY; RIOT3's own
        // order record binds no station to ACT missions at all). Accepting ACT
        // here risked firing pickup/drop on the wrong visit, so ACT is gated
        // out; MOVE arrival covers every observed template, and the reconciler
        // remains the safety net for dropped MOVE webhooks.
        if (type != "MOVE")
            return false;

        if (vendorStationId is not > 0)
            return false;

        var resolvedId = await facilityReadService.ResolveStationByVendorRefAsync(
            vendorStationId.Value.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        var pickupHit = trip.PickupStationId.HasValue && resolvedId == trip.PickupStationId.Value;
        var dropHit = trip.DropStationId.HasValue && resolvedId == trip.DropStationId.Value;

        if (pickupHit)
        {
            if (trip.VendorPickedUpAt is not null) return false;   // fire-once — already picked
            trip.MarkVendorPickedUp(actedAt: actedAt);
            logger.LogInformation(
                "[StationTransition] Trip {TripId} pickup completed at station (vendorId={VendorId}) — items will be marked Picked",
                trip.Id, vendorStationId);
            return true;
        }

        if (dropHit)
        {
            if (trip.VendorDroppedAt is not null) return false;   // fire-once — already dropped

            // Round-trip templates (e.g. STF39_FG_TO_SHELF_FG1) visit the drop
            // station FIRST to fetch the empty rack before heading to pickup.
            // A drop can never precede the pickup, so that fetch visit must not
            // fire — trip 6435 (2026-08-05) stamped VendorDroppedAt on it and
            // OMS rejected the premature droppedoff callback (400 "not ready"),
            // while the real drop visit was then swallowed by the fire-once
            // guard. Gate only when the trip has a pickup leg: legacy rows
            // without PickupStationId can never satisfy it.
            if (trip.PickupStationId.HasValue && trip.VendorPickedUpAt is null)
            {
                logger.LogInformation(
                    "[StationTransition] Trip {TripId} MOVE finished at drop station (vendorId={VendorId}) before pickup — rack-fetch visit on a round-trip template, not firing drop",
                    trip.Id, vendorStationId);
                return false;
            }
            // Resolve the order's POD policy so the integration event carries it
            // (no POD → items land at Delivered; POD → hold at DroppedOff).
            var requiresDropPod = await orderReader.GetRequiresDropPodAsync(
                trip.DeliveryOrderId, cancellationToken);
            trip.MarkVendorDropCompleted(requiresDropPod, actedAt: actedAt);
            logger.LogInformation(
                "[StationTransition] Trip {TripId} drop completed at station (vendorId={VendorId}, requiresDropPod={Pod}) — items will be marked {Target}",
                trip.Id, vendorStationId, requiresDropPod?.ToString() ?? "(null)",
                requiresDropPod == true ? "DroppedOff" : "Delivered");
            return true;
        }

        return false;
    }
}
