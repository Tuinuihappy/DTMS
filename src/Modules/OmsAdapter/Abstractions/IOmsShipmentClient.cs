using DTMS.OmsAdapter.Abstractions.Models;

namespace DTMS.OmsAdapter.Abstractions;

// Outbound notification to the upstream OMS. Implementations MUST throw
// on non-2xx so the calling consumer can surface failure to MassTransit
// retry / dead-letter — never swallow the error and return.
//
// Phase S.6 follow-up — each method takes an OmsCallbackTarget resolved
// per-call (UI-driven iam.SystemCredentials, fallback to env). The client
// no longer pins BaseAddress / Authorization at DI time, so rotating the
// token through the admin UI propagates without redeploy.
public interface IOmsShipmentClient
{
    // POST {target.BaseUrl}/api/shipments — fired when a Trip transitions
    // Created → InProgress (TASK_PROCESSING). Carries shipmentId +
    // deliveryBy in the JSON body so OMS can register the shipment.
    Task NotifyShipmentStartedAsync(OmsCallbackTarget target, OmsShipmentNotification notification, CancellationToken cancellationToken);

    // POST {target.BaseUrl}/api/shipments/{shipmentId}/arrived — fired
    // when a Trip reaches the drop station (SUB_TASK_FINISHED @ drop).
    // shipmentId is the Trip.Id passed in the URL path; the body only
    // carries lots.
    Task NotifyShipmentArrivedAsync(OmsCallbackTarget target, string shipmentId, IReadOnlyList<OmsLot> lots, CancellationToken cancellationToken);
}
