namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Retires still-pending source-callback outbox rows once the callback they
/// would deliver has already been delivered out-of-band — specifically by a
/// manual resend, which dispatches the callback synchronously without touching
/// the outbox table.
///
/// <para>Driving incident: a <c>shipment.started.v1</c> fan-out row failed its
/// first dispatch (bad token → 401 transient) and sat in retry backoff. The
/// operator fixed the token and hit Resend, which delivered the shipment. The
/// still-queued fan-out row then retried with the now-valid token, re-POSTed
/// the same shipment, and got OMS's create-once 400 — classified permanent,
/// which clobbered the resend's green status with a red "failed" card. Marking
/// the pending row superseded the instant the resend succeeds stops that
/// duplicate retry from ever firing.</para>
///
/// <para>Implementation lives in the composition root (DTMS.Api), the only
/// assembly that can see <c>OutboxDbContext</c> — same split as
/// <see cref="ICallbackFormatterResolver"/>. Matches by
/// <c>PartitionKey + Type + RelatedOrderId</c>, order-scoped because every
/// attempt in a retry chain maps to the same shipmentId (root trip) = one
/// shipment at the receiver.</para>
/// </summary>
public interface ISourceCallbackOutboxSuperseder
{
    /// <summary>
    /// Marks every still-pending (unprocessed) partitioned outbox row for
    /// <paramref name="systemKey"/> / <paramref name="eventType"/> /
    /// <paramref name="orderId"/> as superseded, so the outbox processor
    /// never dispatches them. Idempotent: rows the processor already
    /// terminated (success or failure) are skipped. Returns the number of
    /// rows retired (0 when none were pending).
    /// </summary>
    Task<int> SupersedePendingAsync(
        string systemKey, string eventType, Guid orderId, CancellationToken ct);
}
