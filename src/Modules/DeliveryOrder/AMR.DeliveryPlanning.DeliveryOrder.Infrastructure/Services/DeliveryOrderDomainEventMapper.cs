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
                    evt.EventId, evt.OccurredOn, evt.TenantId, evt.OrderId, evt.Priority,
                    evt.Legs.Select(l => new DeliveryLegDto(l.LegId, l.Sequence, l.PickupStationId, l.DropStationId)).ToList())
            ],
            DeliveryOrderCancelledDomainEvent evt =>
            [
                new DeliveryOrderCancelledIntegrationEvent(evt.EventId, evt.OccurredOn, evt.TenantId, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderFailedDomainEvent evt =>
            [
                new DeliveryOrderFailedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.TenantId, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderCompletedDomainEvent evt =>
            [
                new DeliveryOrderCompletedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.TenantId, evt.OrderId)
            ],
            _ => []
        };
    }
}
