using AMR.DeliveryPlanning.Fleet.Domain.Events;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Services;

public class FleetDomainEventMapper : IDomainEventToIntegrationEventMapper
{
    public IReadOnlyCollection<IIntegrationEvent> Map(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            VehicleStateChangedDomainEvent evt =>
            [
                new VehicleStateChangedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.VehicleId,
                    evt.NewState.ToString(),
                    evt.BatteryLevel,
                    evt.CurrentNodeId)
            ],
            VehicleMaintenanceEnteredDomainEvent evt =>
            [
                new VehicleMaintenanceEnteredIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.VehicleId,
                    evt.MaintenanceRecordId)
            ],
            VehicleMaintenanceExitedDomainEvent evt =>
            [
                new VehicleMaintenanceExitedIntegrationEvent(
                    evt.EventId,
                    evt.OccurredOn,
                    evt.VehicleId)
            ],
            _ => []
        };
    }
}
