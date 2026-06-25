using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Events;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

public class GeofenceOverrideRequestTests
{
    private static GeofenceOverrideRequest Submit(TimeSpan? expiresIn = null) =>
        GeofenceOverrideRequest.Submit(
            operatorId: Guid.NewGuid(),
            tripId: Guid.NewGuid(),
            expectedWarehouseId: Guid.NewGuid(),
            reportedLat: 13.7563,
            reportedLng: 100.5018,
            distanceFromGeofenceM: 35.0,
            reason: "GPS drift — operator parked across the street",
            photoUrl: "https://minio/pods/photo.jpg",
            expiresIn: expiresIn ?? TimeSpan.FromMinutes(10));

    [Fact]
    public void Submit_NewRequest_RaisesRequestedEvent()
    {
        var req = Submit();

        req.Status.Should().Be(OverrideRequestStatus.Pending);
        req.DomainEvents.Should().ContainSingle(e => e is GeofenceOverrideRequestedDomainEvent);
    }

    [Fact]
    public void Submit_EmptyReason_Throws()
    {
        var act = () => GeofenceOverrideRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            13.0, 100.0, 50.0, reason: "  ", photoUrl: null,
            expiresIn: TimeSpan.FromMinutes(10));

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Submit_NonPositiveDistance_Throws()
    {
        var act = () => GeofenceOverrideRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            13.0, 100.0, distanceFromGeofenceM: 0,
            reason: "x", photoUrl: null,
            expiresIn: TimeSpan.FromMinutes(10));

        act.Should().Throw<ArgumentException>().WithParameterName("distanceFromGeofenceM");
    }

    [Fact]
    public void Approve_PendingRequest_FlipsStatusAndRaisesEvent()
    {
        var req = Submit();
        req.ClearDomainEvents();
        var supervisorId = Guid.NewGuid();

        req.Approve(supervisorId, note: "verified via photo");

        req.Status.Should().Be(OverrideRequestStatus.Approved);
        req.DecidedByOperatorId.Should().Be(supervisorId);
        req.DecisionNote.Should().Be("verified via photo");
        req.DomainEvents.Should().ContainSingle(e => e is GeofenceOverrideApprovedDomainEvent);
    }

    [Fact]
    public void Deny_RequiresReason()
    {
        var req = Submit();
        var act = () => req.Deny(Guid.NewGuid(), reason: "");
        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Approve_AfterDecision_Throws()
    {
        var req = Submit();
        req.Deny(Guid.NewGuid(), "outside service area");

        var act = () => req.Approve(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*terminal state*");
    }

    [Fact]
    public void MarkExpired_PendingPastDeadline_FlipsToExpired()
    {
        var req = Submit(TimeSpan.FromMinutes(1));
        var future = DateTime.UtcNow.AddMinutes(5);

        req.MarkExpired(asOf: future);

        req.Status.Should().Be(OverrideRequestStatus.Expired);
        req.DecidedAt.Should().Be(future);
    }

    [Fact]
    public void MarkExpired_BeforeDeadline_IsNoOp()
    {
        var req = Submit(TimeSpan.FromMinutes(10));

        req.MarkExpired(asOf: DateTime.UtcNow);     // not yet past expiry

        req.Status.Should().Be(OverrideRequestStatus.Pending);
    }
}
