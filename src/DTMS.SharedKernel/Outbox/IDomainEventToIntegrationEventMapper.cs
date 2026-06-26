using DTMS.SharedKernel.Domain;

namespace DTMS.SharedKernel.Outbox;

public interface IDomainEventToIntegrationEventMapper
{
    IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent);
}
