using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// A source system calls off a trip on one of its OWN orders.
///
/// <para>Unlike the four progress-reporting actions this is not limited to
/// self-managed transport: a DTMS-executed AMR trip is in scope too, and
/// cancelling one issues the real RIOT3 CMD_ORDER_CANCEL — a robot mid-route
/// stops. The reach is deliberate, matching the operator Cancel button.</para>
/// </summary>
/// <param name="SourceSystemKey">
/// Pinned from the authenticated principal by the endpoint, never read from the
/// request body, and checked against the parent order before anything happens.
/// </param>
/// <param name="Reason">
/// Optional. Becomes the order-activity timeline entry and, once that
/// subscription is enabled, the shipment.cancelled.v1 payload — so a blank one
/// is logged as a warning rather than quietly defaulted.
/// </param>
public record SourceCancelTripCommand(
    Guid TripId,
    string SourceSystemKey,
    string ActionBy,
    string? Reason = null,
    DateTime? ActedAt = null) : ICommand, ISourceTripCommand;
