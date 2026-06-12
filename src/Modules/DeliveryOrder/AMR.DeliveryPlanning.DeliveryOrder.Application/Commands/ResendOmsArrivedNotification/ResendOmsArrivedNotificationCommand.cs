using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsArrivedNotification;

/// <summary>
/// Operator-driven manual resend of the upstream-OMS "arrived"
/// notification for a specific trip (the /api/shipments/{id}/arrived
/// endpoint). Use when the automatic consumer dead-lettered the drop
/// notification and the upstream issue has been resolved.
/// </summary>
public record ResendOmsArrivedNotificationCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendOmsArrivedNotificationResult>;

public sealed record ResendOmsArrivedNotificationResult(
    string ShipmentId,
    int LotCount,
    long LatencyMs);
