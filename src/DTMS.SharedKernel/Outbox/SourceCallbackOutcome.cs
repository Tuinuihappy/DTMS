using DTMS.SharedKernel.Domain;

namespace DTMS.SharedKernel.Outbox;

/// <summary>
/// Phase S.5 — emitted by <c>MultiPartitionOutboxProcessor</c> when a source
/// callback row reaches a TERMINAL outcome: delivered (HTTP 2xx/409) or failed
/// after the outbox retry ladder is exhausted. Only produced for rows that
/// carry an order linkage (<c>OutboxMessage.RelatedOrderId</c>), so the owning
/// module can write the per-order audit its UI reads — without the generic
/// dispatch layer depending on that module.
///
/// <para><see cref="Success"/>=false with a 4xx <see cref="StatusCode"/> is a
/// permanent rejection; 5xx / timeout / null is a transient failure that
/// exhausted retries. The consumer maps these to the existing audit event
/// types (e.g. UpstreamOmsNotified / UpstreamOmsRejected).</para>
/// </summary>
public sealed record SourceCallbackOutcome(
    Guid EventId,
    DateTime OccurredOn,
    string SystemKey,
    string CallbackEventType,
    Guid OrderId,
    Guid? TripId,
    bool Success,
    int? StatusCode,
    string? Detail,
    Guid? CorrelationId = null) : IIntegrationEvent;
