using DTMS.Transport.Manual.Application.Services;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

public class PodObjectKeyTests
{
    [Fact]
    public void Generate_Pickup_BuildsCorrectPrefix()
    {
        var tripId = Guid.NewGuid();
        var key = PodObjectKey.Generate(tripId, PodObjectKey.KindPickup, "jpg");

        key.Should().StartWith($"pod/{tripId}/pickup/");
        key.Should().EndWith(".jpg");
    }

    [Fact]
    public void Generate_Drop_BuildsCorrectPrefix()
    {
        var tripId = Guid.NewGuid();
        var key = PodObjectKey.Generate(tripId, PodObjectKey.KindDrop, "png");

        key.Should().StartWith($"pod/{tripId}/drop/");
        key.Should().EndWith(".png");
    }

    [Fact]
    public void Generate_DefaultsExtensionToBin_WhenEmpty()
    {
        var tripId = Guid.NewGuid();
        var key = PodObjectKey.Generate(tripId, PodObjectKey.KindPickup, "");

        key.Should().EndWith(".bin");
    }

    [Fact]
    public void Generate_InvalidKind_Throws()
    {
        var act = () => PodObjectKey.Generate(Guid.NewGuid(), "completed", "jpg");
        act.Should().Throw<ArgumentException>().WithParameterName("kind");
    }

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentKeys()
    {
        var tripId = Guid.NewGuid();
        var k1 = PodObjectKey.Generate(tripId, PodObjectKey.KindPickup, "jpg");
        var k2 = PodObjectKey.Generate(tripId, PodObjectKey.KindPickup, "jpg");
        k1.Should().NotBe(k2);
    }

    [Fact]
    public void BelongsToTripLeg_MatchingPrefix_True()
    {
        var tripId = Guid.NewGuid();
        var key = PodObjectKey.Generate(tripId, PodObjectKey.KindPickup, "jpg");

        PodObjectKey.BelongsToTripLeg(key, tripId, PodObjectKey.KindPickup).Should().BeTrue();
    }

    [Fact]
    public void BelongsToTripLeg_WrongKind_False()
    {
        var tripId = Guid.NewGuid();
        var key = PodObjectKey.Generate(tripId, PodObjectKey.KindPickup, "jpg");

        PodObjectKey.BelongsToTripLeg(key, tripId, PodObjectKey.KindDrop).Should().BeFalse();
    }

    [Fact]
    public void BelongsToTripLeg_DifferentTrip_False()
    {
        var keyForA = PodObjectKey.Generate(Guid.NewGuid(), PodObjectKey.KindPickup, "jpg");

        PodObjectKey.BelongsToTripLeg(keyForA, Guid.NewGuid(), PodObjectKey.KindPickup).Should().BeFalse();
    }
}
