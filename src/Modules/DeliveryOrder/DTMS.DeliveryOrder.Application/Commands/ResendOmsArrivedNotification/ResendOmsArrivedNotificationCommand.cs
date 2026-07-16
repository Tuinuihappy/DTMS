using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendOmsArrivedNotification;

/// <summary>
/// Operator-driven manual resend of the upstream-OMS "arrived"
/// notification for a specific trip (the /api/shipments/{id}/arrived
/// endpoint). Use when the automatic callback
/// (ShipmentArrivedCallbackFanoutConsumer → outbox) exhausted its retries
/// and the upstream issue has been resolved. Dispatched synchronously so
/// the operator sees the result immediately.
/// </summary>
public record ResendOmsArrivedNotificationCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendOmsArrivedNotificationResult>;

public sealed record ResendOmsArrivedNotificationResult(
    string ShipmentId,
    int LotCount,
    long LatencyMs);
