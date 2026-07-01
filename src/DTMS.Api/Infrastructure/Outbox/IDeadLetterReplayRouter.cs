using DTMS.SharedKernel.Outbox;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Phase O3 — knows which per-module DbContext owns a given
/// <see cref="DeadLetterMessage.Source"/> slug. Used by
/// <see cref="DeadLetterStore.ReplayAsync"/> to re-insert an
/// <see cref="OutboxMessage"/> back into the original module's
/// OutboxMessages table (so the standard drain path retries publish
/// from scratch).
///
/// <para>Adding a new module = new DbContext-owning schema — extend
/// the switch in <see cref="DeadLetterReplayRouter"/>. Two lines.</para>
/// </summary>
public interface IDeadLetterReplayRouter
{
    /// <summary>
    /// Insert <paramref name="message"/> into the OutboxMessages table
    /// of the module identified by <paramref name="source"/>, then
    /// SaveChanges. Throws if <paramref name="source"/> is unknown.
    /// </summary>
    Task ReinsertAsync(string source, OutboxMessage message, CancellationToken ct = default);
}
