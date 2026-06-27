using DTMS.OmsAdapter.Abstractions.Models;

namespace DTMS.OmsAdapter.Abstractions;

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
}
