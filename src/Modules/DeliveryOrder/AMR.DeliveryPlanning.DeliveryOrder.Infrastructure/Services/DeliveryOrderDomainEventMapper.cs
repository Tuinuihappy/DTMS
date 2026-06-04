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
                new DeliveryOrderConfirmedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Priority,
                    evt.EarliestUtc, evt.LatestUtc, evt.SubmittedAt,
                    evt.Items.Select(i => new ItemSummaryDto(
                        i.ItemId, i.WeightKg, i.PickupStationId, i.DropStationId,
                        i.Hazmat is { } hz ? new ItemHazmatSummaryDto(hz.ClassCode, hz.PackingGroup) : null,
                        i.Temperature is { } tr ? new ItemTemperatureSummaryDto(tr.MinC, tr.MaxC) : null,
                        i.HandlingInstructions)).ToList(),
                    evt.RequestedTransportMode)
            ],
            DeliveryOrderCancelledDomainEvent evt =>
            [
                new DeliveryOrderCancelledIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderFailedDomainEvent evt =>
            [
                new DeliveryOrderFailedIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderCompletedDomainEvent evt =>
            [
                new DeliveryOrderCompletedIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId)
            ],
            DeliveryOrderPartiallyCompletedDomainEvent evt =>
            [
                new DeliveryOrderPartiallyCompletedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    evt.DeliveredCount, evt.NotDeliveredCount, evt.TotalItems)
            ],
            DeliveryOrderAmendedDomainEvent evt =>
            [
                new DeliveryOrderAmendedIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderHeldDomainEvent evt =>
            [
                new DeliveryOrderHeldIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
            ],
            DeliveryOrderReleasedDomainEvent evt =>
            [
                new DeliveryOrderReleasedIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId)
            ],
            DeliveryOrderRejectedDomainEvent evt =>
            [
                new DeliveryOrderRejectedIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason)
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
