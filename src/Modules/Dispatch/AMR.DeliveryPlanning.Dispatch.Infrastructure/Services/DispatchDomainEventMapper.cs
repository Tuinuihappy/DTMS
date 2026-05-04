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
            TripCompletedDomainEvent evt =>
            [
                new TripCompletedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TenantId,
                    evt.TripId,
                    evt.JobId)
            ],
            TripCancelledDomainEvent evt =>
            [
                new TripCancelledIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TripId,
                    evt.JobId,
                    evt.Reason)
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
                    evt.StopId)
            ],
            _ => []
        };
    }
}
