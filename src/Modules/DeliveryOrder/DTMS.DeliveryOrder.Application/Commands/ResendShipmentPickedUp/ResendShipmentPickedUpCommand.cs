using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentPickedUp;

/// <summary>
/// Operator-driven manual resend of the shipment-pickedup callback (2026-08)
/// to the order's SOURCE SYSTEM (resolved from the order). Use when the
/// automatic callback (ShipmentPickedUpCallbackFanoutConsumer → outbox)
/// exhausted its retries and the upstream issue has been resolved.
/// Dispatched synchronously so the operator sees the result immediately.
/// </summary>
public record ResendShipmentPickedUpCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendShipmentPickedUpResult>;

public sealed record ResendShipmentPickedUpResult(
    string ShipmentId,
    string LocationCode,
    long LatencyMs);
