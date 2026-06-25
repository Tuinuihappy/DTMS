using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

public class GeofenceCalculatorTests
{
    // Bangkok Grand Palace (13.7500, 100.4914) ↔ Wat Pho (13.7465, 100.4928)
    // ~ 400m apart — a known short ground-truth distance.
    [Fact]
    public void DistanceMeters_KnownPoints_ReturnsExpected()
    {
        var d = GeofenceCalculator.DistanceMeters(13.7500, 100.4914, 13.7465, 100.4928);
        d.Should().BeInRange(380, 430);
    }

    [Fact]
    public void DistanceMeters_SamePoint_ReturnsZero()
    {
        var d = GeofenceCalculator.DistanceMeters(13.7563, 100.5018, 13.7563, 100.5018);
        d.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void Check_NoRadius_AlwaysInside()
    {
        var r = GeofenceCalculator.Check(0, 0, 50, 50, radiusM: null);
        r.IsInside.Should().BeTrue();
        r.RadiusM.Should().BeNull();
    }

    [Fact]
    public void Check_WithinRadius_Inside()
    {
        // 1 degree of latitude ≈ 111 km — well outside 500m radius
        // when we offset by 0.0001 degrees (~11m).
        var r = GeofenceCalculator.Check(
            reportedLat: 13.7500 + 0.00005,
            reportedLng: 100.4914 + 0.00005,
            warehouseLat: 13.7500,
            warehouseLng: 100.4914,
            radiusM: 100);
        r.IsInside.Should().BeTrue();
    }

    [Fact]
    public void Check_OutsideRadius_NotInside_OvershootReported()
    {
        var r = GeofenceCalculator.Check(
            reportedLat: 13.7500 + 0.002,    // ≈ 222m north
            reportedLng: 100.4914,
            warehouseLat: 13.7500,
            warehouseLng: 100.4914,
            radiusM: 100);
        r.IsInside.Should().BeFalse();
        r.OvershootM.Should().BeGreaterThan(0);
        r.DistanceM.Should().BeInRange(200, 250);
    }
}
