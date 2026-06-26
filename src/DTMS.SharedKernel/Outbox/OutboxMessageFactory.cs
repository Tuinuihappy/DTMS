using System.Text.Json;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.SharedKernel.Outbox;

public static class OutboxMessageFactory
{
    public static OutboxMessage FromIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        var eventType = integrationEvent.GetType();

        return new OutboxMessage(
            integrationEvent.EventId,
            eventType.AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, eventType),
            integrationEvent.OccurredOn);
    }

    public static OutboxMessage FromIntegrationEvent<T>(T integrationEvent)
        where T : IIntegrationEvent
    {
        return FromIntegrationEvent((IIntegrationEvent)integrationEvent);
    }
}
