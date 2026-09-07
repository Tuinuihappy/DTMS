using DTMS.Fleet.IntegrationEvents;
using DTMS.SharedKernel.Storage;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Fleet.Application.Consumers;

/// <summary>
/// Carries out the delete instruction the outbox recorded when a row went away.
///
/// <para>Never throws for an object that is already gone — delivery is at least
/// once, so a redelivery after a successful pass is normal, and treating that
/// as failure would park the message in the dead-letter queue permanently.</para>
///
/// <para><b>It reports what it removed, not what it was asked to remove.</b>
/// An earlier version logged the length of the key list once the loop ended,
/// which read as success even when this process was pointed at an unreachable
/// endpoint and every object survived. A count of intentions is not evidence
/// of anything.</para>
/// </summary>
public class AttachmentObjectsOrphanedConsumer : IConsumer<AttachmentObjectsOrphanedIntegrationEvent>
{
    private readonly IObjectStorageService _storage;
    private readonly ILogger<AttachmentObjectsOrphanedConsumer> _logger;

    public AttachmentObjectsOrphanedConsumer(
        IObjectStorageService storage,
        ILogger<AttachmentObjectsOrphanedConsumer> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AttachmentObjectsOrphanedIntegrationEvent> context)
    {
        var evt = context.Message;
        var removed = 0;
        var alreadyGone = 0;
        var failed = new List<string>();

        foreach (var key in evt.ObjectKeys)
        {
            // One failing key must not strand the others: a transient error on
            // the first would otherwise leave the rest untried on every retry.
            ObjectDeleteOutcome outcome;
            try
            {
                outcome = await _storage.DeleteAsync(evt.Bucket, key, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deleting orphaned object {Bucket}/{Key} threw.", evt.Bucket, key);
                outcome = ObjectDeleteOutcome.Failed;
            }

            switch (outcome)
            {
                case ObjectDeleteOutcome.Deleted: removed++; break;
                case ObjectDeleteOutcome.AlreadyAbsent: alreadyGone++; break;
                default: failed.Add(key); break;
            }
        }

        if (failed.Count > 0)
        {
            // Warning rather than throwing: retrying helps a transient fault but
            // not a misconfigured endpoint, and a message that can never succeed
            // would cycle to the DLQ and take the working keys with it. The
            // bytes are recoverable — a wrong-looking silence is not.
            _logger.LogWarning(
                "Orphan cleanup incomplete for {Bucket}: {Removed} removed, {AlreadyGone} already gone, " +
                "{Failed} still present ({Keys}).",
                evt.Bucket, removed, alreadyGone, failed.Count, string.Join(", ", failed));
            return;
        }

        _logger.LogInformation(
            "Orphan cleanup for {Bucket}: {Removed} removed, {AlreadyGone} already gone.",
            evt.Bucket, removed, alreadyGone);
    }
}
