using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Domain.Events;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Services;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Domain.Events;
using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

public class DomainEventMapperTests
{
    [Fact]
    public void DeliveryOrderMapper_MapsReadyToPlanEvent()
    {
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var pickupStationId = Guid.NewGuid();
        var dropStationId = Guid.NewGuid();
        var legs = new List<DeliveryLegEventDto>
        {
            new(legId, 1, pickupStationId, dropStationId)
        };

        var integrationEvent = new DeliveryOrderDomainEventMapper()
            .Map(new DeliveryOrderReadyToPlanDomainEvent(
                eventId,
                occurredOn,
                tenantId,
                orderId,
                "High",
                legs))
            .Should()
            .ContainSingle()
            .Subject
            .Should()
            .BeOfType<DeliveryOrderReadyForPlanningIntegrationEvent>()
            .Subject;

        integrationEvent.EventId.Should().Be(eventId);
        integrationEvent.OccurredOn.Should().Be(occurredOn);
        integrationEvent.TenantId.Should().Be(tenantId);
        integrationEvent.DeliveryOrderId.Should().Be(orderId);
        integrationEvent.Priority.Should().Be("High");
        integrationEvent.Legs.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new DeliveryLegDto(legId, 1, pickupStationId, dropStationId));
    }

    [Fact]
    public void PlanningMapper_MapsCommittedJobWithLegSnapshots()
    {
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var fromStationId = Guid.NewGuid();
        var toStationId = Guid.NewGuid();

        var integrationEvent = new PlanningDomainEventMapper()
            .Map(new JobCommittedDomainEvent(
                eventId,
                occurredOn,
                tenantId,
                jobId,
                vehicleId,
                [new CommittedLegSnapshot(fromStationId, toStationId, 1)]))
            .Should()
            .ContainSingle()
            .Subject
            .Should()
            .BeOfType<PlanCommittedIntegrationEvent>()
            .Subject;

        integrationEvent.EventId.Should().Be(eventId);
        integrationEvent.TenantId.Should().Be(tenantId);
        integrationEvent.JobId.Should().Be(jobId);
        integrationEvent.VehicleId.Should().Be(vehicleId);
        integrationEvent.Legs.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PlannedLegDto(fromStationId, toStationId, 1));
    }

    [Fact]
    public void FleetMapper_MapsMaintenanceAndStateEvents()
    {
        var vehicleId = Guid.NewGuid();
        var vehicleTypeId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var maintenanceRecordId = Guid.NewGuid();

        var mapper = new FleetDomainEventMapper();

        mapper.Map(new VehicleStateChangedDomainEvent(
                vehicleId,
                vehicleTypeId,
                VehicleState.Idle,
                VehicleState.Working,
                82.5,
                nodeId))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<VehicleStateChangedIntegrationEvent>()
            .Which.CurrentNodeId.Should().Be(nodeId);

        mapper.Map(new VehicleMaintenanceEnteredDomainEvent(
                vehicleId,
                maintenanceRecordId,
                VehicleState.Idle))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<VehicleMaintenanceEnteredIntegrationEvent>()
            .Which.MaintenanceRecordId.Should().Be(maintenanceRecordId);

        mapper.Map(new VehicleMaintenanceExitedDomainEvent(vehicleId))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<VehicleMaintenanceExitedIntegrationEvent>()
            .Which.VehicleId.Should().Be(vehicleId);
    }

    [Fact]
    public void DispatchMapper_MapsExternalWorkflowEvents()
    {
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var exceptionId = Guid.NewGuid();
        var stopId = Guid.NewGuid();

        var mapper = new DispatchDomainEventMapper();

        mapper.Map(new TripCompletedDomainEvent(eventId, occurredOn, tenantId, tripId, jobId))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TripCompletedIntegrationEvent>()
            .Which.JobId.Should().Be(jobId);

        mapper.Map(new TripCancelledDomainEvent(eventId, occurredOn, tripId, jobId, "operator"))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TripCancelledIntegrationEvent>()
            .Which.Reason.Should().Be("operator");

        mapper.Map(new ExceptionRaisedDomainEvent(eventId, occurredOn, tripId, jobId, exceptionId, "E1", "High", "blocked"))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ExceptionRaisedIntegrationEvent>()
            .Which.Detail.Should().Be("blocked");

        mapper.Map(new PodCapturedDomainEvent(eventId, occurredOn, tripId, stopId))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PodCapturedIntegrationEvent>()
            .Which.StopId.Should().Be(stopId);
    }

    [Fact]
    public void Mappers_ReturnEmpty_ForUnmappedDomainEvents()
    {
        var domainEvent = new UnmappedDomainEvent(Guid.NewGuid(), DateTime.UtcNow);

        new DeliveryOrderDomainEventMapper().Map(domainEvent).Should().BeEmpty();
        new PlanningDomainEventMapper().Map(domainEvent).Should().BeEmpty();
        new FleetDomainEventMapper().Map(domainEvent).Should().BeEmpty();
        new DispatchDomainEventMapper().Map(domainEvent).Should().BeEmpty();
    }

    private sealed record UnmappedDomainEvent(Guid EventId, DateTime OccurredOn) : IDomainEvent;
}
