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
    public void DeliveryOrderMapper_MapsConfirmedEvent()
    {
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var orderId = Guid.NewGuid();
        var pickupStationId = Guid.NewGuid();
        var dropStationId = Guid.NewGuid();
        var deadline = new DateTime(2026, 5, 8, 18, 0, 0, DateTimeKind.Utc);
        var items = new List<ItemEventDto>
        {
            new("SKU-001", 5.5, pickupStationId, dropStationId)
        };

        var result = new DeliveryOrderDomainEventMapper()
            .Map(new DeliveryOrderConfirmedDomainEvent(
                eventId, occurredOn, orderId, "High", deadline, items))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderConfirmedIntegrationEvent>().Subject;

        result.EventId.Should().Be(eventId);
        result.OccurredOn.Should().Be(occurredOn);
        result.DeliveryOrderId.Should().Be(orderId);
        result.Priority.Should().Be("High");
        result.Deadline.Should().Be(deadline);
        result.Items.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ItemSummaryDto("SKU-001", 5.5, pickupStationId, dropStationId));
    }

    [Fact]
    public void DeliveryOrderMapper_MapsCancelledEvent()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper()
            .Map(new DeliveryOrderCancelledDomainEvent(eventId, DateTime.UtcNow, orderId, "Not needed"))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderCancelledIntegrationEvent>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.Reason.Should().Be("Not needed");
    }

    [Fact]
    public void DeliveryOrderMapper_MapsHeldEvent()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper()
            .Map(new DeliveryOrderHeldDomainEvent(eventId, DateTime.UtcNow, orderId, "Awaiting confirmation"))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderHeldIntegrationEvent>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.Reason.Should().Be("Awaiting confirmation");
    }

    [Fact]
    public void DeliveryOrderMapper_MapsReleasedEvent()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper()
            .Map(new DeliveryOrderReleasedDomainEvent(eventId, DateTime.UtcNow, orderId))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderReleasedIntegrationEvent>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
    }

    [Fact]
    public void DeliveryOrderMapper_MapsAmendedEvent()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper()
            .Map(new DeliveryOrderAmendedDomainEvent(eventId, DateTime.UtcNow, orderId, "Adjusted window"))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderAmendedIntegrationEvent>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.Reason.Should().Be("Adjusted window");
    }

    [Fact]
    public void PlanningMapper_MapsCommittedJobWithLegSnapshots()
    {
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var deliveryOrderId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var fromStationId = Guid.NewGuid();
        var toStationId = Guid.NewGuid();

        var result = new PlanningDomainEventMapper()
            .Map(new JobCommittedDomainEvent(
                eventId, occurredOn, jobId, deliveryOrderId, vehicleId,
                [new CommittedLegSnapshot(fromStationId, toStationId, 1)]))
            .Should().ContainSingle().Subject
            .Should().BeOfType<PlanCommittedIntegrationEvent>().Subject;

        result.EventId.Should().Be(eventId);
        result.JobId.Should().Be(jobId);
        result.DeliveryOrderId.Should().Be(deliveryOrderId);
        result.VehicleId.Should().Be(vehicleId);
        result.Legs.Should().ContainSingle()
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
                vehicleId, vehicleTypeId, VehicleState.Idle, VehicleState.Working, 82.5, nodeId))
            .Should().ContainSingle().Which.Should().BeOfType<VehicleStateChangedIntegrationEvent>()
            .Which.CurrentNodeId.Should().Be(nodeId);

        mapper.Map(new VehicleMaintenanceEnteredDomainEvent(vehicleId, maintenanceRecordId, VehicleState.Idle))
            .Should().ContainSingle().Which.Should().BeOfType<VehicleMaintenanceEnteredIntegrationEvent>()
            .Which.MaintenanceRecordId.Should().Be(maintenanceRecordId);

        mapper.Map(new VehicleMaintenanceExitedDomainEvent(vehicleId))
            .Should().ContainSingle().Which.Should().BeOfType<VehicleMaintenanceExitedIntegrationEvent>()
            .Which.VehicleId.Should().Be(vehicleId);
    }

    [Fact]
    public void DispatchMapper_MapsTripCompletedEvent()
    {
        var eventId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var deliveryOrderId = Guid.NewGuid();

        var result = new DispatchDomainEventMapper()
            .Map(new TripCompletedDomainEvent(eventId, DateTime.UtcNow, tripId, jobId, deliveryOrderId))
            .Should().ContainSingle().Subject
            .Should().BeOfType<TripCompletedIntegrationEvent>().Subject;

        result.TripId.Should().Be(tripId);
        result.JobId.Should().Be(jobId);
        result.DeliveryOrderId.Should().Be(deliveryOrderId);
    }

    [Fact]
    public void DispatchMapper_MapsPodCapturedEvent()
    {
        var eventId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();

        var result = new DispatchDomainEventMapper()
            .Map(new PodCapturedDomainEvent(eventId, DateTime.UtcNow, tripId, stopId, ["SKU-001"]))
            .Should().ContainSingle().Subject
            .Should().BeOfType<PodCapturedIntegrationEvent>().Subject;

        result.TripId.Should().Be(tripId);
        result.StopId.Should().Be(stopId);
        result.ScannedIds.Should().Contain("SKU-001");
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
