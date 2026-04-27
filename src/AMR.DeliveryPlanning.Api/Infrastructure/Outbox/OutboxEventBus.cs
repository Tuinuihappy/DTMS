using System.Text.Json;
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
        var message = new OutboxMessage(
            Guid.NewGuid(),
            typeof(T).AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent),
            DateTime.UtcNow);

        _outboxDb.OutboxMessages.Add(message);
        await _outboxDb.SaveChangesAsync(cancellationToken);
    }
}
