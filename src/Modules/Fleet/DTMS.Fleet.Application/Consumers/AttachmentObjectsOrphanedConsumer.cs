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
/// as failure would park the message in the dead-letter queue permanently.
/// <see cref="IObjectStorageService.DeleteAsync"/> already treats absence as
/// success; this only has to avoid inventing a failure of its own.</para>
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

        foreach (var key in evt.ObjectKeys)
        {
            // One failing key must not strand the others: a transient error on
            // the first would otherwise leave the rest untried on every retry.
            try
            {
                await _storage.DeleteAsync(evt.Bucket, key, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not delete orphaned object {Bucket}/{Key}.", evt.Bucket, key);
            }
        }

        _logger.LogInformation(
            "Cleared {Count} orphaned object(s) from {Bucket}.", evt.ObjectKeys.Count, evt.Bucket);
    }
}
