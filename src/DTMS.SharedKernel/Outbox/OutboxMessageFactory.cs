using System.Diagnostics;
using System.Text.Json;
using DTMS.SharedKernel.Domain;

namespace DTMS.SharedKernel.Outbox;

public static class OutboxMessageFactory
{
    public static OutboxMessage FromIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        var eventType = integrationEvent.GetType();

        // Phase O4 — capture the ambient W3C traceparent so the publish
        // span (fired later by OutboxProcessorService, potentially minutes
        // to hours after this row is written) can chain back to whichever
        // handler / request triggered the domain event. Activity.Current.Id
        // is the W3C traceparent string when W3C is the default id-format
        // (Activity.DefaultIdFormat = W3C, set by AspNetCore hosting).
        // Null on background paths with no ambient activity — the publish
        // span then starts as a fresh root, which is still a valid Jaeger
        // trace.
        var traceParent = Activity.Current?.Id;

        return new OutboxMessage(
            integrationEvent.EventId,
            eventType.AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, eventType),
            integrationEvent.OccurredOn,
            traceParent: traceParent);
    }

    public static OutboxMessage FromIntegrationEvent<T>(T integrationEvent)
        where T : IIntegrationEvent
    {
        return FromIntegrationEvent((IIntegrationEvent)integrationEvent);
    }
}
