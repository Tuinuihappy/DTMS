using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentArrived;

/// <summary>
/// Operator-driven manual resend of the shipment-arrived callback to the
/// order's SOURCE SYSTEM (resolved from the order; Phase C removed the OMS
/// pinning). Use when the automatic callback
/// (ShipmentArrivedCallbackFanoutConsumer → outbox) exhausted its retries and
/// the upstream issue has been resolved. Dispatched synchronously so the
/// operator sees the result immediately.
/// </summary>
public record ResendShipmentArrivedCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendShipmentArrivedResult>;

public sealed record ResendShipmentArrivedResult(
    string ShipmentId,
    int LotCount,
    long LatencyMs);
