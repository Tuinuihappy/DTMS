using DTMS.Dispatch.Domain.Entities;
using FluentAssertions;

namespace Dispatch.UnitTests;

// 2026-08 — Pickup/DropLocationCode are frozen onto the Trip at creation
// (the source system's own strings) so the pickedup/droppedoff callbacks
// survive item unbinding on cancel. Never mutated after CreateForEnvelope.
public class TripLocationCodeTests
{
    [Fact]
    public void CreateForEnvelope_StoresTrimmedLocationCodes()
    {
        var trip = Trip.CreateForEnvelope(
            Guid.NewGuid(), "upper-LC1", "ORD-LC",
            pickupLocationCode: " SHELF1 ",
            dropLocationCode: "STF_09");

        trip.PickupLocationCode.Should().Be("SHELF1");
        trip.DropLocationCode.Should().Be("STF_09");
    }

    [Fact]
    public void CreateForEnvelope_DefaultsToNull_AndBlankNormalizesToNull()
    {
        var noCodes = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-LC2", "ORD-LC");
        noCodes.PickupLocationCode.Should().BeNull();
        noCodes.DropLocationCode.Should().BeNull();

        var blank = Trip.CreateForEnvelope(
            Guid.NewGuid(), "upper-LC3", "ORD-LC",
            pickupLocationCode: "  ", dropLocationCode: "");
        blank.PickupLocationCode.Should().BeNull();
        blank.DropLocationCode.Should().BeNull();
    }
}
