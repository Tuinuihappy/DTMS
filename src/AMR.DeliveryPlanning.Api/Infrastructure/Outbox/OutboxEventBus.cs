using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

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
