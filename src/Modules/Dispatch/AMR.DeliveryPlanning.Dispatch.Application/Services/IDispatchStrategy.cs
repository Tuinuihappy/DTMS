using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Per-mode dispatch contract — turns a created <see cref="Trip"/> into
/// an external action (RIOT3 envelope POST for AMR, push notification
/// for Manual, 3PL shipment POST for Fleet). Implementations live in the
/// transport-mode modules; the registry resolves the right one based on
/// the order's <see cref="TransportMode"/>.
///
/// Phase 1 has only the AMR implementation; Phase 4 + 5 add Manual and
/// Fleet without touching Dispatch. The trade-off vs. having a single
/// vendor adapter is that dispatch flows (build request payload, call
/// vendor, persist vendor key) genuinely differ per mode — strategy
/// keeps each mode's flow self-contained instead of forcing if/else
/// chains into Dispatch handlers.
/// </summary>
public interface IDispatchStrategy
{
    /// <summary>Mode this strategy handles. The registry indexes by this.</summary>
    TransportMode Mode { get; }

    /// <summary>
    /// Dispatch the trip to its external system. Returns a structured
    /// result instead of throwing so the caller can decide between
    /// failure types (vendor unreachable vs. vendor rejected vs. data
    /// invalid).
    /// </summary>
    Task<Result<DispatchOutcome>> DispatchAsync(Trip trip, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a dispatch attempt. <see cref="VendorOrderKey"/> is the
/// external id (RIOT3 orderKey for AMR, waybill number for Fleet, null
/// for Manual which has no minted key). Caller persists this back on
/// Trip (Phase 1) or AmrTripExtension (Phase 3+).
/// </summary>
public sealed record DispatchOutcome(string? VendorOrderKey, string? VendorRequestSnapshot);
