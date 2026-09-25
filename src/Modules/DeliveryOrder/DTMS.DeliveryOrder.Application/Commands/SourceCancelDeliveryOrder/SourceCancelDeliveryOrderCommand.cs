using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.SourceCancelDeliveryOrder;

/// <summary>
/// A source system calls off one of its OWN delivery orders, addressed by the
/// caller's own OrderRef rather than the DTMS guid.
///
/// <para>Reaches further than POST /api/v1/source/trips/{id}/cancel: an order in
/// Confirmed / Planning / Planned has no trip for that route to touch. Where
/// trips do exist, OrderCancelledCascadeConsumer calls each of them off — so
/// this is the wider of the two hammers, not a narrower one.</para>
/// </summary>
/// <param name="SourceSystemKey">
/// Pinned from the authenticated principal by the endpoint, never read from the
/// body. It is half of the lookup key, so a caller cannot name another system's
/// order at all — the miss is indistinguishable from "no such ref", which
/// matters because an OrderRef, unlike a trip guid, can be guessed.
/// </param>
/// <param name="ActionBy">
/// Required, for contract parity with the trip route — a caller integrating both
/// cancels writes one payload builder. Note the handler: nothing on the order
/// side persists it.
/// </param>
public record SourceCancelDeliveryOrderCommand(
    string SourceSystemKey,
    string OrderRef,
    string ActionBy,
    string? Reason = null,
    DateTime? ActedAt = null) : ICommand;
