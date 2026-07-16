using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ReopenDeliveryOrder;

/// <summary>
/// Move a Failed or Cancelled delivery order back to Confirmed so its
/// terminal trip(s) can be retried. Admin action — audited via the
/// "OrderReopened" event on the audit trail.
///
/// With <paramref name="AutoRetry"/> (the frontend dialog's default) the
/// handler follows up by reissuing the latest Cancelled/Failed trip of
/// each retry chain via ReissueTripCommand(source: "Reopen") — the same
/// path as the manual /trips/{id}/retry button, so the retry chain
/// (PreviousAttemptId → stable OMS shipmentId), attempt numbering and
/// Job rebind are all preserved. The audit trail still records the two
/// actions separately (OrderReopened + TripRetryEvent). A retry failure
/// never rolls the reopen back — the order stays Confirmed for a manual
/// retry.
/// </summary>
public record ReopenDeliveryOrderCommand(
    Guid OrderId,
    string ReopenedBy,
    string Reason,
    bool AutoRetry = false
) : ICommand<ReopenOrderResult>;

/// <summary>Outcome surfaced to the caller (endpoint → frontend toast).</summary>
public record ReopenOrderResult(
    int ReinstatedItems,
    int RetriedTrips,
    IReadOnlyList<string> RetryErrors);
