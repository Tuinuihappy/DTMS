using DTMS.Transport.Manual.Domain.Entities;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

public class ManualTripExtensionTests
{
    private static ManualTripExtension AssignFresh() =>
        ManualTripExtension.AssignToOperator(
            tripId: Guid.NewGuid(),
            operatorId: Guid.NewGuid(),
            ackDeadline: DateTime.UtcNow.AddMinutes(15),
            pickupDeadline: DateTime.UtcNow.AddHours(1),
            dropDeadline: DateTime.UtcNow.AddHours(3));

    [Fact]
    public void AssignToOperator_CapturesAllFields()
    {
        var ext = AssignFresh();

        ext.TripId.Should().NotBe(Guid.Empty);
        ext.OperatorId.Should().NotBe(Guid.Empty);
        ext.AcknowledgedAt.Should().BeNull();
        ext.PickedUpAt.Should().BeNull();
        ext.DroppedAt.Should().BeNull();
        ext.AckDeadline.Should().NotBeNull();
    }

    [Fact]
    public void AssignToOperator_EmptyTripId_Throws()
    {
        var act = () => ManualTripExtension.AssignToOperator(
            Guid.Empty, Guid.NewGuid(), null, null, null);
        act.Should().Throw<ArgumentException>().WithParameterName("tripId");
    }

    [Fact]
    public void MarkAcknowledged_FirstCall_StampsTimestamp()
    {
        var ext = AssignFresh();
        ext.MarkAcknowledged();
        ext.AcknowledgedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkAcknowledged_Twice_IsIdempotent()
    {
        var ext = AssignFresh();
        ext.MarkAcknowledged();
        var first = ext.AcknowledgedAt;
        Thread.Sleep(2);
        ext.MarkAcknowledged();
        ext.AcknowledgedAt.Should().Be(first);     // first write wins
    }

    [Fact]
    public void MarkPickedUp_BeforeAcknowledged_Throws()
    {
        var ext = AssignFresh();
        var act = () => ext.MarkPickedUp(podKey: null, overrideId: null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*acknowledg*");
    }

    [Fact]
    public void MarkDropped_BeforePickup_Throws()
    {
        var ext = AssignFresh();
        ext.MarkAcknowledged();
        var act = () => ext.MarkDropped(podKey: null, overrideId: null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*pickup*");
    }

    [Fact]
    public void HappyPath_AckPickupDrop_AllStamped()
    {
        var ext = AssignFresh();
        var pickupOverride = Guid.NewGuid();

        ext.MarkAcknowledged();
        ext.MarkPickedUp(podKey: "pickup/123", overrideId: pickupOverride);
        ext.MarkDropped(podKey: "drop/456", overrideId: null);

        ext.AcknowledgedAt.Should().NotBeNull();
        ext.PickedUpAt.Should().NotBeNull();
        ext.DroppedAt.Should().NotBeNull();
        ext.PickupPodKey.Should().Be("pickup/123");
        ext.PickupGeofenceOverrideId.Should().Be(pickupOverride);
        ext.DropPodKey.Should().Be("drop/456");
        ext.DropGeofenceOverrideId.Should().BeNull();
    }
}
