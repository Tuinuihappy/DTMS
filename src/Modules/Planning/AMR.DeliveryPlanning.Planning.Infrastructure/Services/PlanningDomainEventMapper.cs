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
            // Existing — PlanCommitted carries the leg payload for the
            // Planning-trigger consumers downstream. Keep emitting BOTH this
            // and the new JobCommitted v1 event so older consumers don't
            // break while new projection consumers prefer the V1 family.
            JobCommittedDomainEvent evt =>
            [
                new PlanCommittedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.JobId,
                    evt.DeliveryOrderId,
                    evt.VehicleId,
                    evt.Legs
                        .Where(l => l.FromStationId != Guid.Empty && l.ToStationId != Guid.Empty)
                        .Select(l => new PlannedLegDto(l.FromStationId, l.ToStationId, l.SequenceOrder))
                        .ToList())
            ],

            // Phase P1 (b12) — the rest of the Job lifecycle. Each domain
            // event maps to one V1 integration event so the
            // JobStatusHistoryProjector materialises the full timeline.
            JobCreatedDomainEvent evt =>
            [
                new JobCreatedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId)
            ],

            JobDispatchedDomainEvent evt =>
            [
                new JobDispatchedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId,
                    evt.TripId, evt.VendorOrderKey, evt.AttemptNumber)
            ],

            JobExecutingDomainEvent evt =>
            [
                new JobExecutingIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId, evt.TripId)
            ],

            JobCompletedDomainEvent evt =>
            [
                new JobCompletedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId, evt.TripId)
            ],

            JobFailedDomainEvent evt =>
            [
                new JobFailedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId,
                    evt.Reason, evt.AttemptNumber,
                    // Cross-module wire format = string, not enum, so consumers
                    // in other modules don't take a ref on Planning.Domain.
                    FailureCategory: evt.Category.ToString())
            ],

            JobCancelledDomainEvent evt =>
            [
                new JobCancelledIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId,
                    evt.TripId, evt.Reason,
                    FailureCategory: evt.Category.ToString())
            ],

            JobPausedDomainEvent evt =>
            [
                new JobPausedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId, evt.TripId)
            ],

            JobResumedDomainEvent evt =>
            [
                new JobResumedIntegrationEventV1(
                    evt.EventId, evt.OccurredOn, evt.JobId, evt.DeliveryOrderId, evt.TripId)
            ],

            // JobAssignedDomainEvent is also a status transition (→ Assigned),
            // but the existing JobAssignedIntegrationEvent shape doesn't carry
            // the data the projector needs in a stable way (PickupStationId /
            // DropStationId are mandatory there but optional in domain). For
            // P1 we route Assigned through the projector via a synthetic event
            // — added here when an Assigned-specific use case lands.

            _ => []
        };
    }
}
