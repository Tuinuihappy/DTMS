using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;

public class DeliveryOrderDomainEventMapper : IDomainEventToIntegrationEventMapper
{
    public IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            DeliveryOrderReadyToPlanDomainEvent evt =>
            [
                new DeliveryOrderReadyForPlanningIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TenantId,
                    evt.OrderId,
                    evt.Priority,
                    evt.PickupStationId,
                    evt.DropStationId)
            ],
            _ => []
        };
    }
}
