using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmItemPod;

/// <summary>
/// POD (Proof of Delivery) scan for a single item at a given checkpoint.
///
/// ScanType:
///   • Pickup — operator at pickup station; audit-only, never flips
///     Item.Status (vendor pickup signal already drove Pending→Picked).
///   • Drop   — operator at drop dock; transitions Picked/DroppedOff →
///     Delivered when the order has RequiresDropPod=true.
///
/// Method values: "Barcode" / "Manual" / "Signature" / "Confirm".
/// Reference: barcode value / signature hash / typed code; null for
/// "Confirm" method.
/// </summary>
public record ConfirmItemPodCommand(
    Guid OrderId,
    Guid ItemId,
    PodScanType ScanType,
    string ScannedBy,
    string Method,
    string? Reference) : ICommand<ConfirmItemPodResult>;

public sealed record ConfirmItemPodResult(
    bool Confirmed,
    string ItemStatus,
    bool OrderTerminalReached);
