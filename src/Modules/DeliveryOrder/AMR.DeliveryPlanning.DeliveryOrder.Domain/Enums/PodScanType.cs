namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

/// <summary>
/// Where on the chain-of-custody a POD scan was recorded.
///
/// Pickup — operator at the pickup station scans the item as it is loaded
///          onto the robot. Audit-only: does not flip Item.Status (vendor
///          Picked signal already handled that).
/// Drop   — operator at the drop dock scans the delivered item. When the
///          order has RequiresDropPod=true this transitions
///          Picked/DroppedOff → Delivered. With drop POD off the scan is
///          still recorded for audit but Status was already Delivered via
///          MarkTripItemsDeliveredOrLeaveForPod.
/// </summary>
public enum PodScanType
{
    Pickup,
    Drop
}
