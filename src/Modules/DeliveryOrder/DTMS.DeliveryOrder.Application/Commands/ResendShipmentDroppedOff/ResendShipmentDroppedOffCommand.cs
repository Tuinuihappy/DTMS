using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentDroppedOff;

/// <summary>
/// Operator-driven manual resend of the shipment-droppedoff callback
/// (2026-08, renamed from ResendShipmentArrived when OMS moved the route) to
/// the order's SOURCE SYSTEM. Use when the automatic callback
/// (ShipmentDroppedOffCallbackFanoutConsumer → outbox) exhausted its retries
/// and the upstream issue has been resolved. Dispatched synchronously so the
/// operator sees the result immediately.
/// </summary>
public record ResendShipmentDroppedOffCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendShipmentDroppedOffResult>;

public sealed record ResendShipmentDroppedOffResult(
    string ShipmentId,
    string LocationCode,
    long LatencyMs);
