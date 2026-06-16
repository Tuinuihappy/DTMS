using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsPodCompletedNotification;

/// <summary>
/// Phase OMS B4 — Manual resend of "pod-completed" notification. Uses
/// items currently bound to the trip as scannedIds (same data the
/// automatic consumer would have sent if RIOT3 re-delivered the
/// PodCaptured event). Trip should typically be Completed.
/// </summary>
public record ResendOmsPodCompletedNotificationCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendOmsPodCompletedNotificationResult>;

public sealed record ResendOmsPodCompletedNotificationResult(
    string ShipmentId,
    int ScannedCount,
    long LatencyMs);
