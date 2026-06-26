using DTMS.Transport.Manual.Domain.Entities;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

// Phase 4.6 — ManualTripExtension.ReassignToOperator domain method.
// Pins the dispatcher reassign flow's invariants:
//   - Empty Guid rejected
//   - Same-operator is idempotent (no exception)
//   - Reassign before pickup clears ack + pickup POD progress so the
//     new operator starts from scratch
//   - Reassign AFTER pickup keeps the POD photos (they're already
//     valid evidence; only the drop is outstanding)
//   - Refuses to reassign once the trip has been dropped
//   - Drop deadline preserved (customer SLA is operator-agnostic)
public class ManualTripExtensionReassignTests
{
    private static ManualTripExtension Fresh(Guid? oldOpId = null) =>
        ManualTripExtension.AssignToOperator(
            tripId: Guid.NewGuid(),
            operatorId: oldOpId ?? Guid.NewGuid(),
            ackDeadline: DateTime.UtcNow.AddMinutes(5),
            pickupDeadline: DateTime.UtcNow.AddMinutes(35),
            dropDeadline: DateTime.UtcNow.AddHours(2));

    [Fact]
    public void Reassign_EmptyOperatorId_Throws()
    {
        var ext = Fresh();
        var act = () => ext.ReassignToOperator(Guid.Empty, null, null);
        act.Should().Throw<ArgumentException>().WithParameterName("newOperatorId");
    }

    [Fact]
    public void Reassign_SameOperator_IsIdempotent()
    {
        var opId = Guid.NewGuid();
        var ext = Fresh(opId);
        var origAck = ext.AckDeadline;

        ext.ReassignToOperator(opId, DateTime.UtcNow.AddMinutes(10), null);

        ext.OperatorId.Should().Be(opId);
        ext.AckDeadline.Should().Be(origAck);   // unchanged
    }

    [Fact]
    public void Reassign_BeforePickup_ResetsAckAndPickupPodAndOverride()
    {
        var ext = Fresh();
        ext.MarkAcknowledged();
        var newOp = Guid.NewGuid();

        ext.ReassignToOperator(newOp, DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddMinutes(40));

        ext.OperatorId.Should().Be(newOp);
        ext.AcknowledgedAt.Should().BeNull();
        ext.PickupPodKey.Should().BeNull();
        ext.PickupGeofenceOverrideId.Should().BeNull();
    }

    [Fact]
    public void Reassign_AfterPickup_KeepsPodPhotos()
    {
        var ext = Fresh();
        ext.MarkAcknowledged();
        ext.MarkPickedUp(podKey: "pod/pickup/abc", overrideId: null);

        ext.ReassignToOperator(Guid.NewGuid(), null, null);

        ext.PickedUpAt.Should().NotBeNull();
        ext.PickupPodKey.Should().Be("pod/pickup/abc");
    }

    [Fact]
    public void Reassign_AfterDrop_Throws()
    {
        var ext = Fresh();
        ext.MarkAcknowledged();
        ext.MarkPickedUp(podKey: null, overrideId: null);
        ext.MarkDropped(podKey: "pod/drop/xyz", overrideId: null);

        var act = () => ext.ReassignToOperator(Guid.NewGuid(), null, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*dropped*");
    }

    [Fact]
    public void Reassign_DropDeadlinePreserved()
    {
        var ext = Fresh();
        var origDrop = ext.DropDeadline;

        ext.ReassignToOperator(Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(5),
            DateTime.UtcNow.AddMinutes(35));

        ext.DropDeadline.Should().Be(origDrop);
    }
}
