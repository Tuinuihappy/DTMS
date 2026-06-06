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
            // Option A: Planning + Planned are sub-second transitions —
            // surface in audit but don't broadcast to other modules.
            DeliveryOrderPlanningStartedDomainEvent  => [],
            DeliveryOrderPlannedDomainEvent          => [],
            // Dispatched + InProgress have meaningful durations and are
            // useful to frontend / dashboards — publish via outbox.
            DeliveryOrderDispatchedDomainEvent evt =>
            [
                new DeliveryOrderDispatchedIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId)
            ],
            DeliveryOrderInProgressDomainEvent evt =>
            [
                new DeliveryOrderInProgressIntegrationEventV1(evt.EventId, evt.OccurredOn, evt.OrderId)
            ],
            // Reopen is an admin action that brings Failed → Confirmed for
            // retry. We intentionally do NOT re-fire DeliveryOrderConfirmed
            // here — the operator must explicitly call /trips/{id}/retry
            // so the audit trail separates "who reopened" from "who retried".
            DeliveryOrderReopenedDomainEvent         => [],
            // Redispatch is the "no Trip ever materialised" recovery path
            // (e.g. every group failed dispatch). The Redispatch domain
            // method re-fires DeliveryOrderConfirmedDomainEvent alongside
            // this audit-only event, so Planning's consumer wakes up
            // again. We don't publish this one to integration — it's a
            // local audit marker.
            DeliveryOrderRedispatchedDomainEvent     => [],

            // Item-level lifecycle events fired by the trip-aware item
            // methods. They're useful for audit + analytics but no other
            // module needs to react — kept internal until a consumer asks.
            TripItemsAssignedDomainEvent             => [],
            TripItemsDeliveredDomainEvent            => [],
            TripItemsFailedDomainEvent               => [],
            TripItemsPickedDomainEvent               => [],

            _ => throw new InvalidOperationException(
                $"Unhandled domain event '{domainEvent.GetType().Name}'. " +
                "Add a mapping or explicitly return [] if internal-only.")
        };
    }
}
