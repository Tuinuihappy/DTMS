using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;

namespace AMR.DeliveryPlanning.OmsAdapter.Abstractions;

// Outbound notification to the upstream OMS. Implementations MUST throw
// on non-2xx so the calling consumer can surface failure to MassTransit
// retry / dead-letter — never swallow the error and return.
public interface IOmsShipmentClient
{
    // POST /api/shipments — fired when a Trip transitions Created →
    // InProgress (TASK_PROCESSING). Carries shipmentId + deliveryBy in
    // the JSON body so OMS can register the shipment.
    Task NotifyShipmentStartedAsync(OmsShipmentNotification notification, CancellationToken cancellationToken);

    // POST /api/shipments/{shipmentId}/arrived — fired when a Trip
    // reaches the drop station (SUB_TASK_FINISHED @ drop). shipmentId is
    // the Trip.Id passed in the URL path; the body only carries lots.
    Task NotifyShipmentArrivedAsync(string shipmentId, IReadOnlyList<OmsLot> lots, CancellationToken cancellationToken);

    // Phase OMS B4 — POST /api/shipments/{shipmentId}/failed. Fired when
    // a Trip enters the terminal Failed state (vendor/system incident).
    // shipmentId path-segment matches the /started + /arrived convention
    // — receiver uses it to mark the shipment unrecoverable + open an
    // incident on its side.
    Task NotifyShipmentFailedAsync(string shipmentId, OmsTripFailedNotification body, CancellationToken cancellationToken);

    // Phase OMS B4 — POST /api/shipments/{shipmentId}/cancelled. Fired
    // on operator-driven cancellation. Distinct from /failed so the
    // receiver can branch (no auto-retry on cancellation).
    Task NotifyShipmentCancelledAsync(string shipmentId, OmsTripCancelledNotification body, CancellationToken cancellationToken);

    // Phase OMS B4 — POST /api/shipments/{shipmentId}/pod-completed.
    // Fired after POD scan completes. Separate from /arrived: arrived =
    // physical presence at drop; pod-completed = final proof of delivery.
    Task NotifyShipmentPodCompletedAsync(string shipmentId, OmsPodCompletedNotification body, CancellationToken cancellationToken);
}
