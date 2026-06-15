namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Projections;

/// <summary>
/// Phase P5.3 read model — one row per (TripId, ItemPk) binding.
/// Written exclusively by <c>TripItemsProjector</c> from Dispatch Trip
/// lifecycle events. Backs <c>GET /api/v1/dispatch/trips/{id}/items</c>.
///
/// OrderRef/OrderStatus are snapshotted at trip-start and never refreshed
/// (operator can re-fetch order state via <c>DeliveryOrderId</c> if they
/// need live status). ItemStatus is updated on terminal Trip events
/// (Completed → Delivered, Failed/Cancelled → Unbound).
/// </summary>
public class TripItemsRow
{
    public Guid TripId { get; private set; }
    public Guid ItemPk { get; private set; }
    public Guid EventId { get; private set; }
    public Guid DeliveryOrderId { get; private set; }
    public string OrderRef { get; private set; } = string.Empty;
    public string OrderStatus { get; private set; } = string.Empty;
    public string LotNo { get; private set; } = string.Empty;
    public int ItemSeq { get; private set; }
    public string ItemStatus { get; private set; } = string.Empty;
    public string? PickupCode { get; private set; }
    public string? DropCode { get; private set; }
    public double? WeightKg { get; private set; }
    public DateTime BoundAt { get; private set; }
    public DateTime LastEventAt { get; private set; }

    private TripItemsRow() { }   // EF

    public TripItemsRow(
        Guid tripId, Guid itemPk, Guid eventId,
        Guid deliveryOrderId, string orderRef, string orderStatus,
        string lotNo, int itemSeq, string itemStatus,
        string? pickupCode, string? dropCode, double? weightKg,
        DateTime boundAt, DateTime lastEventAt)
    {
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId is required.", nameof(tripId));
        if (itemPk == Guid.Empty)
            throw new ArgumentException("ItemPk is required.", nameof(itemPk));
        if (string.IsNullOrWhiteSpace(orderRef))
            throw new ArgumentException("OrderRef is required.", nameof(orderRef));
        if (string.IsNullOrWhiteSpace(itemStatus))
            throw new ArgumentException("ItemStatus is required.", nameof(itemStatus));

        TripId = tripId;
        ItemPk = itemPk;
        EventId = eventId;
        DeliveryOrderId = deliveryOrderId;
        OrderRef = orderRef;
        OrderStatus = orderStatus;
        LotNo = lotNo;
        ItemSeq = itemSeq;
        ItemStatus = itemStatus;
        PickupCode = pickupCode;
        DropCode = dropCode;
        WeightKg = weightKg;
        BoundAt = boundAt;
        LastEventAt = lastEventAt;
    }

    public void RefreshItemStatus(string newStatus, DateTime lastEventAt)
    {
        if (string.IsNullOrWhiteSpace(newStatus))
            throw new ArgumentException("newStatus is required.", nameof(newStatus));
        ItemStatus = newStatus;
        LastEventAt = lastEventAt;
    }
}
