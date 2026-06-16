using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsTripCancelledNotification;

/// <summary>
/// Phase OMS B4 — Manual resend of "trip-cancelled" notification. Trip
/// must currently be in the Cancelled state. The resend uses the
/// requesting operator as cancelledBy and "operator-requested resend"
/// as the reason since the original cancellation reason isn't stored on
/// the Trip entity (only on the integration event audit).
/// </summary>
public record ResendOmsTripCancelledNotificationCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendOmsTripCancelledNotificationResult>;

public sealed record ResendOmsTripCancelledNotificationResult(
    string ShipmentId,
    long LatencyMs);
