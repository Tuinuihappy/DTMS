using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using FluentAssertions;

namespace Dispatch.UnitTests;

public class TripTests
{
    private static Trip NewEnvelopeTrip(string upperKey = "abc123-G1") =>
        Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, "ORD-1");

    [Fact]
    public void CreateForEnvelope_ProducesCreatedTripWithUpperKey()
    {
        var trip = NewEnvelopeTrip();

        trip.Status.Should().Be(TripStatus.Created);
        trip.UpperKey.Should().Be("abc123-G1");
        trip.VendorOrderKey.Should().Be("ORD-1");
        trip.JobId.Should().Be(Guid.Empty);
        trip.VehicleId.Should().BeNull();
        trip.Events.Should().ContainSingle(e => e.EventType == "EnvelopeDispatched");
    }

    [Fact]
    public void CreateForEnvelope_AllowsEmptyVendorOrderKey()
    {
        // RIOT3 occasionally returns 200 OK with no orderKey. Correlation
        // still works via UpperKey alone.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", null);

        trip.VendorOrderKey.Should().BeNull();
        trip.UpperKey.Should().Be("abc-G1");
    }

    [Fact]
    public void CreateForEnvelope_RejectsEmptyUpperKey()
    {
        var act = () => Trip.CreateForEnvelope(Guid.NewGuid(), "", "ORD");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkVendorStarted_FromCreated_TransitionsToInProgressAndBindsVehicle()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();

        trip.MarkVendorStarted(vehicle);

        trip.Status.Should().Be(TripStatus.InProgress);
        trip.VehicleId.Should().Be(vehicle);
        trip.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkVendorStarted_DuplicateWebhook_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(Guid.NewGuid());
        var startedAt = trip.StartedAt;

        var act = () => trip.MarkVendorStarted(Guid.NewGuid());

        act.Should().NotThrow();
        trip.StartedAt.Should().Be(startedAt);
    }

    [Fact]
    public void MarkVendorCompleted_FromCreated_CompletesAndFiresEventWithUpperKey()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorCompleted();

        trip.Status.Should().Be(TripStatus.Completed);
        trip.CompletedAt.Should().NotBeNull();
        var evt = trip.DomainEvents.OfType<TripCompletedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.DeliveryOrderId.Should().Be(trip.DeliveryOrderId);
    }

    [Fact]
    public void MarkVendorCompleted_AlreadyCompleted_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();
        var eventCount = trip.DomainEvents.OfType<TripCompletedDomainEvent>().Count();

        var act = () => trip.MarkVendorCompleted();

        act.Should().NotThrow();
        trip.DomainEvents.OfType<TripCompletedDomainEvent>().Count().Should().Be(eventCount);
    }

    [Fact]
    public void MarkVendorCompleted_FromCancelled_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.Cancel("ops");

        var act = () => trip.MarkVendorCompleted();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkVendorFailed_FromCreated_FailsAndFiresEvent()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorFailed("path blocked");

        trip.Status.Should().Be(TripStatus.Failed);
        trip.FailureReason.Should().Be("path blocked");
        var evt = trip.DomainEvents.OfType<TripFailedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.Reason.Should().Be("path blocked");
    }

    [Fact]
    public void MarkVendorFailed_AlreadyFailed_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorFailed("first");
        var act = () => trip.MarkVendorFailed("second");
        act.Should().NotThrow();
        trip.FailureReason.Should().Be("first");
    }

    [Fact]
    public void MarkVendorFailed_FromCompleted_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();

        var act = () => trip.MarkVendorFailed("late");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetAssignedVehicle_FirstAssignment_Binds()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();

        trip.SetAssignedVehicle(vehicle);

        trip.VehicleId.Should().Be(vehicle);
    }

    [Fact]
    public void SetAssignedVehicle_SameVehicle_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();
        trip.SetAssignedVehicle(vehicle);

        var act = () => trip.SetAssignedVehicle(vehicle);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetAssignedVehicle_DifferentVehicle_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.SetAssignedVehicle(Guid.NewGuid());

        var act = () => trip.SetAssignedVehicle(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PauseAndResume_RoundTrip()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.Pause();
        trip.Status.Should().Be(TripStatus.Paused);

        trip.Resume();
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public void Cancel_FromCreated_Cancels()
    {
        var trip = NewEnvelopeTrip();

        trip.Cancel("operator");

        trip.Status.Should().Be(TripStatus.Cancelled);
    }
}
