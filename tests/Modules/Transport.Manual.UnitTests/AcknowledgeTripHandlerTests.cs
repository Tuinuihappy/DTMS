using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class AcknowledgeTripHandlerTests
{
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly ITripItemSnapshotProvider _snapshots = Substitute.For<ITripItemSnapshotProvider>();

    private AcknowledgeTripCommandHandler CreateSut()
    {
        _snapshots.GetForTripAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                  .Returns(Array.Empty<TripItemSnapshot>());
        return new(_extensions, _trips, _snapshots);
    }

    [Fact]
    public async Task Handle_NoExtension_FailsWithMessage()
    {
        _extensions.GetByTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                   .Returns((ManualTripExtension?)null);
        var sut = CreateSut();

        var result = await sut.Handle(
            new AcknowledgeTripCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no Manual extension");
    }

    [Fact]
    public async Task Handle_DifferentOperator_Fails()
    {
        var tripId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, owner, null, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);
        var sut = CreateSut();

        var result = await sut.Handle(new AcknowledgeTripCommand(tripId, other), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("different operator");
    }

    [Fact]
    public async Task Handle_HappyPath_MarksExtensionAcknowledged_TransitionsTripToInProgress_PassesItemSnapshot()
    {
        var owner = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(),
            upperKey: "UK-ACK-1",
            vendorOrderKey: null);
        var ext = ManualTripExtension.AssignToOperator(trip.Id, owner, null, null, null);
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(ext);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);
        var snapshot = new TripItemSnapshot(
            ItemPk: Guid.NewGuid(), ItemSeq: 1, LotNo: "L1", ItemStatus: "Pending",
            PickupCode: "WH-A", DropCode: "WH-B",
            WeightKg: null, DeliveryOrderId: trip.DeliveryOrderId,
            OrderRef: "DO-X", OrderStatus: "Dispatched");
        _snapshots.GetForTripAsync(trip.Id, Arg.Any<CancellationToken>())
                  .Returns(new[] { snapshot });

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, owner), default);

        result.IsSuccess.Should().BeTrue();
        ext.AcknowledgedAt.Should().NotBeNull();
        trip.Status.Should().Be(DTMS.Dispatch.Domain.Enums.TripStatus.InProgress);
        await _trips.Received(1).UpdateAsync(trip, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoubleAcknowledge_DoesNotReTransitionTrip()
    {
        var owner = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(), upperKey: "UK-DUP-1", vendorOrderKey: null);
        var ext = ManualTripExtension.AssignToOperator(trip.Id, owner, null, null, null);
        ext.MarkAcknowledged();   // already acknowledged
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(ext);
        var sut = CreateSut();

        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, owner), default);

        result.IsSuccess.Should().BeTrue();
        // Trip aggregate must not be touched on the duplicate.
        await _trips.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _trips.DidNotReceive().UpdateAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>());
    }
}
