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
        var triggeredBy = _actor.Current.TriggeredBy;
        var correlationId = _actor.Current.CorrelationId;

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
                    Items: evt.Items)
            ],
            // WMS PR-4b — Manual/Fleet pool dispatch. Maps to the new
            // integration event that TripStartedOmsNotifyConsumer subscribes
            // to alongside TripStartedIntegrationEvent.
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
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            TripDropCompletedDomainEvent evt =>
            [
                new TripDropCompletedIntegrationEvent(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.DeliveryOrderId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId,
                    RequiresDropPod: evt.RequiresDropPod)
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
                    CorrelationId: correlationId)
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
                    CorrelationId: correlationId)
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
                    CorrelationId: correlationId)
            ],
            // Phase P1 (b12) — pause/resume transitions for the Trip status
            // timeline. Domain payload carries only TripId; the projector
            // pulls DeliveryOrderId from the latest history row when needed.
            TripPausedDomainEvent evt =>
            [
                new TripPausedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId,
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
            ],
            TripResumedDomainEvent evt =>
            [
                new TripResumedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.TripId,
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
