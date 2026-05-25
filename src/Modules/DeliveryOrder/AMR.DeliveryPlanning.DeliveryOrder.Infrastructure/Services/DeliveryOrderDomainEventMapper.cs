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
            DeliveryOrderConfirmedDomainEvent evt =>
            [
                new DeliveryOrderConfirmedIntegrationEvent(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Priority, evt.SlaTier,
                    evt.Earliest, evt.Latest, evt.SubmittedAt,
                    evt.Items.Select(i => new ItemSummaryDto(i.Sku, i.WeightKg, i.PickupStationId, i.DropStationId)).ToList())
            ],
            DeliveryOrderCancelledDomainEvent evt =>
            [
                new DeliveryOrderCancelledIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderFailedDomainEvent evt =>
            [
                new DeliveryOrderFailedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderCompletedDomainEvent evt =>
            [
                new DeliveryOrderCompletedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId)
            ],
            DeliveryOrderAmendedDomainEvent evt =>
            [
                new DeliveryOrderAmendedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderHeldDomainEvent evt =>
            [
                new DeliveryOrderHeldIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderReleasedDomainEvent evt =>
            [
                new DeliveryOrderReleasedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId)
            ],
            DeliveryOrderRejectedDomainEvent evt =>
            [
                new DeliveryOrderRejectedIntegrationEvent(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],

            // internal-only — no cross-module publishing needed
            DeliveryOrderDraftedDomainEvent         => [],
            DeliveryOrderDraftUpdatedDomainEvent     => [],
            DeliveryOrderSubmittedDomainEvent        => [],
            DeliveryOrderValidatedDomainEvent        => [],
            DeliveryOrderPlanningStartedDomainEvent  => [],
            DeliveryOrderPlannedDomainEvent          => [],
            DeliveryOrderDispatchedDomainEvent       => [],
            DeliveryOrderInProgressDomainEvent       => [],

            _ => throw new InvalidOperationException(
                $"Unhandled domain event '{domainEvent.GetType().Name}'. " +
                "Add a mapping or explicitly return [] if internal-only.")
        };
    }
}
