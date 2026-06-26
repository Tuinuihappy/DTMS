using DTMS.SharedKernel.Domain;

namespace DTMS.SharedKernel.Messaging;

public interface IEventBus
{
    Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent;
}
