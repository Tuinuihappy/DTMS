using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Auth;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;

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
                    CorrelationId: correlationId)
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
                    TriggeredBy: triggeredBy, CorrelationId: correlationId)
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
