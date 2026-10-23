using DTMS.Dispatch.Domain.Events;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Outbox;

namespace DTMS.Dispatch.Infrastructure.Services;

// Phase P0 (2026-06-14) — mapper enriches Trip lifecycle integration events
// with TriggeredBy + CorrelationId from the ambient ICurrentActorContext.
// Trip events are typically triggered by RIOT3 webhooks (vendor-webhook
// source) or operator actions (http source) — both flow through the same
// ambient context lookup. ExceptionRaised + PodCaptured are not status
// transitions so they intentionally skip the enrichment.

public class DispatchDomainEventMapper : IDomainEventToIntegrationEventMapper
{
    private readonly ICurrentActorContext _actor;

    public DispatchDomainEventMapper(ICurrentActorContext actor)
    {
        _actor = actor;
    }

    public IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent)
    {
        var actor = _actor.Current;
        var triggeredBy = actor.TriggeredBy;
        var correlationId = actor.CorrelationId;
        // Same shape as DeliveryOrderDomainEventMapper, so a trip row in the
        // order timeline names its actor exactly like an order row does.
        var channel = actor.Channel.ToString();
        var displayName = string.IsNullOrWhiteSpace(actor.DisplayName) ? null : actor.DisplayName;

        return domainEvent switch
        {
            TripStartedDomainEvent evt =>
            [
                new TripStartedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    // JobId is no longer part of the envelope flow — kept on
                    // the integration event for backward compat. Pass the
                    // VehicleId (or Empty when unknown) so consumers that
                    // care about robot binding still receive it.
                    JobId: Guid.Empty,
                    VehicleId: evt.VehicleId ?? Guid.Empty,
                    DeliveryOrderId: evt.DeliveryOrderId,
                    // V1.1 enrichment — TripFactsProjector consumes this to
                    // populate bi.TripFacts.VendorVehicleKey, which the
                    // Vehicle performance report groups by.
                    VendorVehicleKey: evt.VendorVehicleKey,
                    TriggeredBy: triggeredBy,
                    CorrelationId: correlationId,
                    // V1.2 enrichment (Phase P5.3) — Items snapshot for
                    // TripItemsProjector. Pass-through from the domain event
                    // (caller populated via ITripItemSnapshotProvider).
                    Items: evt.Items,
                    Channel: channel, DisplayName: displayName)
            ],
            // WMS PR-4b — Manual pool dispatch. Consumed by the SignalR
            // pool broadcaster + TripItems projector (no upstream callback —
            // shipment.started fires at operator claim via TripStarted).
            TripDispatchedDomainEvent evt =>
            [
                new TripDispatchedIntegrationEventV1(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.DeliveryOrderId,
                    TriggeredBy: triggeredBy,
                    CorrelationId: correlationId,
                    Items: evt.Items)
            ],
            TripPickupCompletedDomainEvent evt =>
            [
                new TripPickupCompletedIntegrationEvent(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.DeliveryOrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            TripDropCompletedDomainEvent evt =>
            [
                new TripDropCompletedIntegrationEvent(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.DeliveryOrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    RequiresDropPod: evt.RequiresDropPod,
                    Channel: channel, DisplayName: displayName)
            ],
            TripCompletedDomainEvent evt =>
            [
                new TripCompletedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.DeliveryOrderId,
                    evt.VendorUpperKey,
                    TriggeredBy: triggeredBy,
                    CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            TripFailedDomainEvent evt =>
            [
                new TripFailedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.DeliveryOrderId,
                    evt.Reason,
                    evt.VendorUpperKey,
                    TriggeredBy: triggeredBy,
                    CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            TripRejectedDomainEvent evt =>
            [
                new TripRejectedIntegrationEventV1(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.DeliveryOrderId,
                    evt.Reason,
                    evt.VendorUpperKey,
                    TriggeredBy: triggeredBy,
                    CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            TripCancelledDomainEvent evt =>
            [
                new TripCancelledIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.DeliveryOrderId,
                    evt.Reason,
                    evt.VendorUpperKey,
                    TriggeredBy: triggeredBy,
                    CorrelationId: correlationId,
                    Channel: channel, DisplayName: displayName)
            ],
            // Phase P1 (b12) — pause/resume transitions for the Trip status
            // timeline. Domain payload carries only TripId; the projector
            // pulls DeliveryOrderId from the latest history row when needed.
            TripResumedDomainEvent evt =>
            [
                new TripResumedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            TripHangDomainEvent evt =>
            [
                new TripHangIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.Reflavour,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            TripHeldDomainEvent evt =>
            [
                new TripHeldIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.Reflavour,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            // Operator acknowledged a robot waiting at a checkpoint (PASS).
            // Status unchanged on the Trip — the projector still appends a
            // history row so the operator's intervention shows in the timeline.
            TripRobotPassAcknowledgedDomainEvent evt =>
            [
                new TripRobotPassAcknowledgedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.VendorVehicleKey,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            // Late robot recovery on a terminal trip — TripFactsProjector
            // patches bi.TripFacts.VendorVehicleKey off this event.
            TripVehicleBackfilledDomainEvent evt =>
            [
                new TripVehicleBackfilledIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.VendorVehicleKey,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            ExceptionRaisedDomainEvent evt =>
            [
                new ExceptionRaisedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.ExceptionId,
                    evt.Code,
                    evt.Severity,
                    evt.Detail)
            ],
            PodCapturedDomainEvent evt =>
            [
                new PodCapturedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.StopId,
                    evt.ScannedIds)
            ],
            _ => []
        };
    }
}
