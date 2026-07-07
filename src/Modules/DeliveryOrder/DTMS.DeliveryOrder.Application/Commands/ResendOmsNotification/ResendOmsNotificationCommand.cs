using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;

/// <summary>
/// Operator-driven manual resend of the upstream-OMS shipment notification
/// for a specific trip. Use when MassTransit's automatic retry has
/// dead-lettered (UpstreamOmsNotifyFailed audit) and the underlying OMS
/// issue has since been resolved.
///
/// Unlike <see cref="Consumers.TripStartedOmsNotifyConsumer"/>, this path
/// calls the OMS client directly so the operator sees immediate
/// feedback — no message queue / retry indirection. The upstream is
/// expected to dedupe by shipmentId, so re-firing on a row that
/// actually succeeded is harmless.
/// </summary>
public record ResendOmsNotificationCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendOmsNotificationResult>;

public sealed record ResendOmsNotificationResult(
    string ShipmentId,
    // Null for self-managed orders — the source system executes transport
    // itself, so there is no vendor vehicle (DeliveryBy sent to OMS as null).
    string? DeliveryBy,
    int LotCount,
    long LatencyMs);
