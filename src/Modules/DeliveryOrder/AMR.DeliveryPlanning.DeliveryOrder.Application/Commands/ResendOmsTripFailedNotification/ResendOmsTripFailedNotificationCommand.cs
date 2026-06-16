using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsTripFailedNotification;

/// <summary>
/// Phase OMS B4 — Manual resend of the upstream-OMS "trip-failed"
/// notification (POST /api/shipments/{shipmentId}/failed). Use when the
/// auto consumer dead-lettered AND the upstream issue is resolved.
/// Trip must be in a terminal Failed state; the resend re-fires
/// Trip.FailureReason verbatim so the upstream record matches the
/// original event payload.
/// </summary>
public record ResendOmsTripFailedNotificationCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendOmsTripFailedNotificationResult>;

public sealed record ResendOmsTripFailedNotificationResult(
    string ShipmentId,
    long LatencyMs);
