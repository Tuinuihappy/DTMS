using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Outbox;

namespace DTMS.Api.Infrastructure.Outbox;

public class OutboxEventBus : IEventBus
{
    private readonly OutboxDbContext _outboxDb;

    public OutboxEventBus(OutboxDbContext outboxDb)
    {
        _outboxDb = outboxDb;
    }

    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        _outboxDb.OutboxMessages.Add(OutboxMessageFactory.FromIntegrationEvent(integrationEvent));
        await _outboxDb.SaveChangesAsync(cancellationToken);
    }
}
