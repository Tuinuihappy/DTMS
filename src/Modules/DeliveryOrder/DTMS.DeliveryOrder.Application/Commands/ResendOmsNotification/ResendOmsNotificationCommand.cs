using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;

/// <summary>
/// Operator-driven manual resend of the upstream-OMS shipment notification
/// for a specific trip. Use when the automatic callback has exhausted its
/// outbox retries (UpstreamOmsNotifyFailed audit) and the underlying OMS
/// issue has since been resolved.
///
/// <para>Unlike the automatic path (ShipmentStartedCallbackFanoutConsumer →
/// outbox → MultiPartitionOutboxProcessor), this one formats the same
/// payload but dispatches it SYNCHRONOUSLY through
/// <c>ISourceCallbackDispatcher</c>, so the operator sees the result
/// immediately — no queue / retry indirection. OMS dedupes by shipmentId
/// (409 = success), so re-firing a row that actually succeeded is
/// harmless.</para>
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
