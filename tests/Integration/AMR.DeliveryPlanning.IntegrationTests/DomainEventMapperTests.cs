using DTMS.DeliveryOrder.Domain.Events;
using DTMS.DeliveryOrder.Infrastructure.Services;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Domain.Events;
using DTMS.Dispatch.Infrastructure.Services;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Events;
using DTMS.Fleet.Infrastructure.Services;
using DTMS.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Domain.Events;
using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using DTMS.Planning.IntegrationEvents;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Domain;
using FluentAssertions;
using NSubstitute;

namespace AMR.DeliveryPlanning.IntegrationTests;

public class DomainEventMapperTests
{
    // P0 added an ICurrentActorContext dependency to every domain-event mapper
    // so projections can stamp who triggered each transition. These tests
    // predate that change and don't care about actor enrichment — give every
    // mapper a stub that returns the system actor so the test focus stays on
    // the event-shape mapping.
    private static ICurrentActorContext Actor()
    {
        var actor = Substitute.For<ICurrentActorContext>();
        actor.Current.Returns(ActorContext.System);
        return actor;
    }

    [Fact]
    public void DeliveryOrderMapper_MapsConfirmedEvent_ToV1()
    {
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var orderId = Guid.NewGuid();
        var pickupStationId = Guid.NewGuid();
        var dropStationId = Guid.NewGuid();
        var earliest = new DateTime(2026, 5, 8, 8, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 5, 8, 18, 0, 0, DateTimeKind.Utc);
        var submittedAt = new DateTime(2026, 5, 7, 23, 0, 0, DateTimeKind.Utc);
        var items = new List<ItemEventDto>
        {
            new("SKU-001", 5.5, pickupStationId, dropStationId)
        };

        var result = new DeliveryOrderDomainEventMapper(Actor())
            .Map(new DeliveryOrderConfirmedDomainEvent(
                eventId, occurredOn, orderId, "High",
                earliest, latest, submittedAt, items))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderConfirmedIntegrationEventV1>().Subject;

        result.EventId.Should().Be(eventId);
        result.OccurredOn.Should().Be(occurredOn);
        result.DeliveryOrderId.Should().Be(orderId);
        result.Priority.Should().Be("High");
        result.EarliestUtc.Should().Be(earliest);
        result.LatestUtc.Should().Be(latest);
        result.SubmittedAt.Should().Be(submittedAt);
        result.SchemaVersion.Should().Be("1.0");
        result.Items.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ItemSummaryDto("SKU-001", 5.5, pickupStationId, dropStationId));
    }

    [Fact]
    public void DeliveryOrderMapper_MapsCancelledEvent_ToV1()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper(Actor())
            .Map(new DeliveryOrderCancelledDomainEvent(eventId, DateTime.UtcNow, orderId, "Not needed"))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderCancelledIntegrationEventV1>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.Reason.Should().Be("Not needed");
        result.SchemaVersion.Should().Be("1.1");
    }

    [Fact]
    public void DeliveryOrderMapper_MapsHeldEvent_ToV1()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper(Actor())
            .Map(new DeliveryOrderHeldDomainEvent(eventId, DateTime.UtcNow, orderId, "Awaiting confirmation"))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderHeldIntegrationEventV1>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.Reason.Should().Be("Awaiting confirmation");
        result.SchemaVersion.Should().Be("1.1");
    }

    [Fact]
    public void DeliveryOrderMapper_MapsReleasedEvent_ToV1()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper(Actor())
            .Map(new DeliveryOrderReleasedDomainEvent(eventId, DateTime.UtcNow, orderId))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderReleasedIntegrationEventV1>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.SchemaVersion.Should().Be("1.1");
    }

    [Fact]
    public void DeliveryOrderMapper_MapsAmendedEvent_ToV1()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = new DeliveryOrderDomainEventMapper(Actor())
            .Map(new DeliveryOrderAmendedDomainEvent(eventId, DateTime.UtcNow, orderId, "Adjusted window"))
            .Should().ContainSingle().Subject
            .Should().BeOfType<DeliveryOrderAmendedIntegrationEventV1>().Subject;

        result.DeliveryOrderId.Should().Be(orderId);
        result.Reason.Should().Be("Adjusted window");
        result.SchemaVersion.Should().Be("1.1");
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

        var result = new PlanningDomainEventMapper(Actor())
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

        var result = new DispatchDomainEventMapper(Actor())
            .Map(new TripCompletedDomainEvent(eventId, DateTime.UtcNow, tripId, jobId, deliveryOrderId, "test-upper-key"))
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

        var result = new DispatchDomainEventMapper(Actor())
            .Map(new PodCapturedDomainEvent(eventId, DateTime.UtcNow, tripId, stopId, ["SKU-001"]))
            .Should().ContainSingle().Subject
            .Should().BeOfType<PodCapturedIntegrationEvent>().Subject;

        result.TripId.Should().Be(tripId);
        result.StopId.Should().Be(stopId);
        result.ScannedIds.Should().Contain("SKU-001");
    }

    [Fact]
    public void PlanningMapper_DropsSyntheticEmptyFromLegs()
    {
        // Planning command handlers create a synthetic first leg with
        // FromStationId = Guid.Empty to represent "vehicle's current
        // position". That sentinel must not leak past the integration
        // boundary — Dispatch / VendorAdapter cannot resolve it to a
        // vendor target, so the task would be silently dropped at the
        // adapter and never reach RIOT3.
        var eventId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var deliveryOrderId = Guid.NewGuid();
        var pickupStationId = Guid.NewGuid();
        var dropStationId = Guid.NewGuid();

        var result = new PlanningDomainEventMapper(Actor())
            .Map(new JobCommittedDomainEvent(
                eventId, DateTime.UtcNow, jobId, deliveryOrderId, null,
                [
                    new CommittedLegSnapshot(Guid.Empty, pickupStationId, 1),
                    new CommittedLegSnapshot(pickupStationId, dropStationId, 2)
                ]))
            .Should().ContainSingle().Subject
            .Should().BeOfType<PlanCommittedIntegrationEvent>().Subject;

        result.Legs.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PlannedLegDto(pickupStationId, dropStationId, 2));
    }

    [Fact]
    public void PlanningFleetDispatchMappers_ReturnEmpty_ForUnmappedDomainEvents()
    {
        // Planning, Fleet, and Dispatch mappers fall through to [] for any
        // domain event they don't have a handler for — the integration-event
        // surface stays opt-in.
        var domainEvent = new UnmappedDomainEvent(Guid.NewGuid(), DateTime.UtcNow);

        new PlanningDomainEventMapper(Actor()).Map(domainEvent).Should().BeEmpty();
        new FleetDomainEventMapper().Map(domainEvent).Should().BeEmpty();
        new DispatchDomainEventMapper(Actor()).Map(domainEvent).Should().BeEmpty();
    }

    [Fact]
    public void DeliveryOrderMapper_Throws_ForUnmappedDomainEvent()
    {
        // DeliveryOrder mapper is intentionally strict — every concrete
        // DeliveryOrder*DomainEvent must be either mapped to an integration
        // event or explicitly listed as internal-only `=> []`. A truly
        // unrecognized type signals a missing entry, so the mapper throws.
        var domainEvent = new UnmappedDomainEvent(Guid.NewGuid(), DateTime.UtcNow);

        var act = () => new DeliveryOrderDomainEventMapper(Actor()).Map(domainEvent);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unhandled domain event 'UnmappedDomainEvent'*");
    }

    private sealed record UnmappedDomainEvent(Guid EventId, DateTime OccurredOn) : IDomainEvent;
}
