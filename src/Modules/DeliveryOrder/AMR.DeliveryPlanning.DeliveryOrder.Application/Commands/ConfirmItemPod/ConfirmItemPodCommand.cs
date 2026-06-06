using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmItemPod;

/// <summary>
/// POD (Proof of Delivery) confirmation for a single item on a
/// RequiresPod order. Operator submits Method + optional Reference;
/// the handler transitions the item DroppedOff/Picked → Delivered,
/// stamps audit columns, and re-computes the order status.
///
/// Method values: "Barcode" / "Manual" / "Signature" / "Confirm".
/// Reference: barcode value / signature hash / typed code; null for
/// "Confirm" method.
/// </summary>
public record ConfirmItemPodCommand(
    Guid OrderId,
    Guid ItemId,
    string ScannedBy,
    string Method,
    string? Reference) : ICommand<ConfirmItemPodResult>;

public sealed record ConfirmItemPodResult(
    bool Confirmed,
    string ItemStatus,
    bool OrderTerminalReached);
