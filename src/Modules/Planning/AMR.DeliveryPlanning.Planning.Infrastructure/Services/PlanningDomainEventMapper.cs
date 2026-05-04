using AMR.DeliveryPlanning.Planning.Domain.Events;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class PlanningDomainEventMapper : IDomainEventToIntegrationEventMapper
{
    public IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            JobCommittedDomainEvent evt =>
            [
                new PlanCommittedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.TenantId,
                    evt.JobId,
                    evt.VehicleId,
                    evt.Legs
                        .Select(l => new PlannedLegDto(l.FromStationId, l.ToStationId, l.SequenceOrder))
                        .ToList())
            ],
            _ => []
        };
    }
}
