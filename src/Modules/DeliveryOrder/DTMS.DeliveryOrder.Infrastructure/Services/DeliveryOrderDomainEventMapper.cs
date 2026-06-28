using DTMS.DeliveryOrder.Domain.Events;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Outbox;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

// Phase P0 (2026-06-14) — mapper enriches every published integration
// event with TriggeredBy + CorrelationId from the ambient
// ICurrentActorContext. These fields are nullable on the receiving side,
// so old consumers ignore them and new projectors can populate
// history.triggered_by directly. See docs/event-projection-implementation-plan.md.

public class DeliveryOrderDomainEventMapper : IDomainEventToIntegrationEventMapper
{
    private readonly ICurrentActorContext _actor;

    public DeliveryOrderDomainEventMapper(ICurrentActorContext actor)
    {
        _actor = actor;
    }

    public IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent)
    {
        // Snapshot once per mapping so every event emitted from a single
        // SaveChanges call shares the same trigger context — important so
        // a multi-event aggregate (Reopen → Released + Confirmed) reads
        // as a single operator action in the audit timeline.
        var actor = _actor.Current;
        var triggeredBy = actor.TriggeredBy;
        var correlationId = actor.CorrelationId;
        // S.1 follow-up — pass channel as string so downstream JSON-only
        // consumers don't need to depend on the SharedKernel enum type.
        // Empty/whitespace DisplayName collapses to null on the wire so
        // the read DTO can render a placeholder consistently.
        var channel = actor.Channel.ToString();
        var displayName = string.IsNullOrWhiteSpace(actor.DisplayName) ? null : actor.DisplayName;

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
                        i.HandlingInstructions,
                        i.PickupWarehouseId, i.DropWarehouseId)).ToList(),
                    evt.RequestedTransportMode)
            ],
            DeliveryOrderCancelledDomainEvent evt =>
            [
                new DeliveryOrderCancelledIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderFailedDomainEvent evt =>
            [
                new DeliveryOrderFailedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderCompletedDomainEvent evt =>
            [
                new DeliveryOrderCompletedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderPartiallyCompletedDomainEvent evt =>
            [
                new DeliveryOrderPartiallyCompletedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    evt.DeliveredCount, evt.NotDeliveredCount, evt.TotalItems,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderAmendedDomainEvent evt =>
            [
                new DeliveryOrderAmendedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderHeldDomainEvent evt =>
            [
                new DeliveryOrderHeldIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderReleasedDomainEvent evt =>
            [
                new DeliveryOrderReleasedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderRejectedDomainEvent evt =>
            [
                new DeliveryOrderRejectedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId, evt.Reason,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],

            // Phase P4.5 — surface early lifecycle to the OrderListView
            // projection so Draft/Submitted/Validated orders appear in the
            // list table. Created carries the full snapshot (the row creator);
            // Submitted/Validated are status-only updates.
            DeliveryOrderCreatedDomainEvent evt =>
            [
                new DeliveryOrderCreatedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    evt.OrderRef, evt.SourceSystem, evt.Status, evt.Priority,
                    evt.RequestedTransportMode, evt.RequestedBy, evt.CreatedBy, evt.Notes,
                    evt.EarliestUtc, evt.LatestUtc, evt.SubmittedAt,
                    evt.RequiresDropPod, evt.RequiresPickupPod,
                    evt.TotalItems, evt.TotalQuantity, evt.TotalWeightKg,
                    evt.Items.Select(i => new ItemSummaryDto(
                        i.ItemId, i.WeightKg, i.PickupStationId, i.DropStationId,
                        i.Hazmat is { } hz ? new ItemHazmatSummaryDto(hz.ClassCode, hz.PackingGroup) : null,
                        i.Temperature is { } tr ? new ItemTemperatureSummaryDto(tr.MinC, tr.MaxC) : null,
                        i.HandlingInstructions,
                        i.PickupWarehouseId, i.DropWarehouseId)).ToList(),
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderSubmittedDomainEvent evt =>
            [
                new DeliveryOrderSubmittedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderValidatedDomainEvent evt =>
            [
                new DeliveryOrderValidatedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            // internal-only — no cross-module publishing needed
            DeliveryOrderDraftedDomainEvent         => [],

            // Phase P4.6 — Draft replace mutates items + totals; surface to
            // the OrderListView projection so the list row reflects the new
            // shape immediately. No cross-module consumer beyond the
            // projector cares (Draft is pre-Planning).
            DeliveryOrderDraftUpdatedDomainEvent evt =>
            [
                new DeliveryOrderDraftUpdatedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            // Option A: Planning + Planned are sub-second transitions —
            // surface in audit but don't broadcast to other modules.
            DeliveryOrderPlanningStartedDomainEvent  => [],
            DeliveryOrderPlannedDomainEvent          => [],
            // Dispatched + InProgress have meaningful durations and are
            // useful to frontend / dashboards — publish via outbox.
            DeliveryOrderDispatchedDomainEvent evt =>
            [
                new DeliveryOrderDispatchedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            DeliveryOrderInProgressDomainEvent evt =>
            [
                new DeliveryOrderInProgressIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.OrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
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
            TripItemsDroppedOffDomainEvent           => [],
            ItemPodRecordedDomainEvent               => [],

            _ => throw new InvalidOperationException(
                $"Unhandled domain event '{domainEvent.GetType().Name}'. " +
                "Add a mapping or explicitly return [] if internal-only.")
        };
    }
}
