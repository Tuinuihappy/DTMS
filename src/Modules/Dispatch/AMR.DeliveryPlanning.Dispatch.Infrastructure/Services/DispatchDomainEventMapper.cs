using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;

public class DispatchDomainEventMapper : IDomainEventToIntegrationEventMapper
{
    public IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent)
    {
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
                    DeliveryOrderId: evt.DeliveryOrderId)
            ],
            TripPickupCompletedDomainEvent evt =>
            [
                new TripPickupCompletedIntegrationEvent(
                    evt.EventId, evt.OccurredOn, evt.TripId, evt.DeliveryOrderId)
            ],
            TripCompletedDomainEvent evt =>
            [
                new TripCompletedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.DeliveryOrderId,
                    evt.VendorUpperKey)
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
                    evt.VendorUpperKey)
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
                    evt.VendorUpperKey)
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
