using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.Admin.ApproveGeofenceOverride;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.Admin.DenyGeofenceOverride;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class ApproveGeofenceOverrideHandlerTests
{
    private readonly IGeofenceOverrideRequestRepository _overrides =
        Substitute.For<IGeofenceOverrideRequestRepository>();

    private static GeofenceOverrideRequest BuildPending() =>
        GeofenceOverrideRequest.Submit(
            operatorId: Guid.NewGuid(),
            tripId: Guid.NewGuid(),
            expectedWarehouseId: Guid.NewGuid(),
            reportedLat: 13.7,
            reportedLng: 100.5,
            distanceFromGeofenceM: 75,
            reason: "GPS drift",
            photoUrl: null,
            expiresIn: TimeSpan.FromMinutes(15));

    [Fact]
    public async Task Approve_MissingRecord_Fails()
    {
        _overrides.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                  .Returns((GeofenceOverrideRequest?)null);
        var sut = new ApproveGeofenceOverrideCommandHandler(_overrides);

        var result = await sut.Handle(
            new ApproveGeofenceOverrideCommand(Guid.NewGuid(), Guid.NewGuid(), null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Approve_HappyPath_FlipsStatusAndSaves()
    {
        var req = BuildPending();
        _overrides.GetByIdAsync(req.Id, Arg.Any<CancellationToken>()).Returns(req);
        var sut = new ApproveGeofenceOverrideCommandHandler(_overrides);

        var result = await sut.Handle(
            new ApproveGeofenceOverrideCommand(req.Id, Guid.NewGuid(), "verified via photo"), default);

        result.IsSuccess.Should().BeTrue();
        req.Status.Should().Be(OverrideRequestStatus.Approved);
        _overrides.Received(1).Update(req);
        await _overrides.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_AlreadyDecided_FailsWithDomainMessage()
    {
        var req = BuildPending();
        req.Deny(Guid.NewGuid(), "earlier denial");
        _overrides.GetByIdAsync(req.Id, Arg.Any<CancellationToken>()).Returns(req);
        var sut = new ApproveGeofenceOverrideCommandHandler(_overrides);

        var result = await sut.Handle(
            new ApproveGeofenceOverrideCommand(req.Id, Guid.NewGuid(), null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("terminal state");
    }
}

public class DenyGeofenceOverrideHandlerTests
{
    private readonly IGeofenceOverrideRequestRepository _overrides =
        Substitute.For<IGeofenceOverrideRequestRepository>();

    [Fact]
    public async Task Deny_EmptyReason_Fails()
    {
        var sut = new DenyGeofenceOverrideCommandHandler(_overrides);
        var result = await sut.Handle(
            new DenyGeofenceOverrideCommand(Guid.NewGuid(), Guid.NewGuid(), ""), default);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("reason");
    }

    [Fact]
    public async Task Deny_HappyPath_FlipsStatusAndSaves()
    {
        var req = GeofenceOverrideRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            13.7, 100.5, 50, "GPS drift", null, TimeSpan.FromMinutes(10));
        _overrides.GetByIdAsync(req.Id, Arg.Any<CancellationToken>()).Returns(req);
        var sut = new DenyGeofenceOverrideCommandHandler(_overrides);

        var result = await sut.Handle(
            new DenyGeofenceOverrideCommand(req.Id, Guid.NewGuid(), "outside service area"), default);

        result.IsSuccess.Should().BeTrue();
        req.Status.Should().Be(OverrideRequestStatus.Denied);
        req.DecisionNote.Should().Be("outside service area");
        _overrides.Received(1).Update(req);
    }
}
