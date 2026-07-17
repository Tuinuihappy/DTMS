using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentStarted;

/// <summary>
/// Operator-driven manual resend of the shipment-started callback to the
/// order's SOURCE SYSTEM (resolved from the order — oms today, sap/erp the
/// moment they subscribe; Phase C removed the OMS pinning). Use when the
/// automatic callback exhausted its outbox retries (UpstreamNotifyFailed
/// audit) and the upstream issue has since been resolved.
///
/// <para>Unlike the automatic path (ShipmentStartedCallbackFanoutConsumer →
/// outbox → MultiPartitionOutboxProcessor), this one formats the same payload
/// but dispatches it SYNCHRONOUSLY through <c>ISourceCallbackDispatcher</c>,
/// so the operator sees the result immediately — no queue / retry
/// indirection. Upstreams dedupe by shipmentId (409 = success), so re-firing
/// a row that actually succeeded is harmless.</para>
/// </summary>
public record ResendShipmentStartedCommand(
    Guid OrderId,
    Guid TripId,
    string? RequestedBy) : ICommand<ResendShipmentStartedResult>;

public sealed record ResendShipmentStartedResult(
    string ShipmentId,
    // Null for self-managed orders — the source system executes transport
    // itself, so there is no vendor vehicle (DeliveryBy sent as null).
    string? DeliveryBy,
    int LotCount,
    long LatencyMs);
