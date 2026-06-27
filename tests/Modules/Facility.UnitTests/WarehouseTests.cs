using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Events;
using DTMS.Facility.Domain.ValueObjects;
using FluentAssertions;

namespace Facility.UnitTests;

// Phase 2.1 — Warehouse aggregate tests. The aggregate is the
// shared-kernel anchor for multi-mode transport (per ADR-002): every
// transport mode references a warehouse, AMR-specific stations move
// under it in Phase 2.3. These tests pin the invariants that future
// phases rely on.

public class WarehouseAggregateTests
{
    // ─── Factory ──────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidData_ProducesActiveWarehouseAndRaisesEvent()
    {
        var warehouse = NewBangkok();

        warehouse.Code.Should().Be("WH-BKK-01");
        warehouse.Name.Should().Be("Bangkok DC");
        warehouse.IsActive.Should().BeTrue();
        warehouse.ServiceModes.Should().BeEquivalentTo(new[] { TransportMode.Amr });
        warehouse.DomainEvents.OfType<WarehouseCreatedDomainEvent>()
            .Should().ContainSingle()
            .Which.Code.Should().Be("WH-BKK-01");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCode_Throws(string code)
    {
        var act = () => Warehouse.Create(
            code, "Bangkok DC",
            new LatLng(13.7, 100.5),
            new Address("123 Sukhumvit Rd"),
            new[] { TransportMode.Amr });

        act.Should().Throw<ArgumentException>().WithParameterName("code");
    }

    [Fact]
    public void Create_NoServiceModes_Throws()
    {
        // A warehouse that serves no transport mode is nonsensical — nothing
        // can ever be dispatched to it. Caller must provide ≥ 1 mode.
        var act = () => Warehouse.Create(
            "WH-BKK-01", "Bangkok DC",
            new LatLng(13.7, 100.5),
            new Address("123 Sukhumvit Rd"),
            Array.Empty<TransportMode>());

        act.Should().Throw<ArgumentException>().WithParameterName("serviceModes");
    }

    [Fact]
    public void Create_DuplicateServiceModes_DedupsAutomatically()
    {
        var warehouse = Warehouse.Create(
            "WH-BKK-01", "Bangkok DC",
            new LatLng(13.7, 100.5),
            new Address("123 Sukhumvit Rd"),
            new[] { TransportMode.Amr, TransportMode.Amr, TransportMode.Manual });

        warehouse.ServiceModes.Should().BeEquivalentTo(
            new[] { TransportMode.Amr, TransportMode.Manual });
    }

    [Fact]
    public void Create_DefaultsHoursToAlwaysOpen()
    {
        // Hours is optional — most warehouses don't need finely-tuned
        // operating windows on day one. AlwaysOpen is a safer default
        // than "closed all the time" which would silently reject all
        // dispatches.
        var warehouse = NewBangkok();

        warehouse.Hours.IsOpenAt(new DateTime(2026, 6, 23, 3, 0, 0)).Should().BeTrue();
        warehouse.Hours.IsOpenAt(new DateTime(2026, 6, 23, 23, 59, 0)).Should().BeTrue();
    }

    // ─── ServiceMode toggle ───────────────────────────────────────────────

    [Fact]
    public void EnableServiceMode_NewMode_AddsAndRaisesEvent()
    {
        var warehouse = NewBangkok();

        warehouse.EnableServiceMode(TransportMode.Manual);

        warehouse.ServesMode(TransportMode.Manual).Should().BeTrue();
        warehouse.DomainEvents.OfType<WarehouseServiceModeEnabledDomainEvent>()
            .Should().ContainSingle().Which.Mode.Should().Be(TransportMode.Manual);
    }

    [Fact]
    public void EnableServiceMode_AlreadyEnabled_IsIdempotent()
    {
        // Re-enabling an existing mode shouldn't raise duplicate events
        // — callers (e.g. retry on a flaky admin endpoint) shouldn't
        // need to track state to be safe.
        var warehouse = NewBangkok();

        warehouse.EnableServiceMode(TransportMode.Amr);

        warehouse.DomainEvents.OfType<WarehouseServiceModeEnabledDomainEvent>()
            .Should().BeEmpty();
    }

    [Fact]
    public void DisableServiceMode_LastMode_ThrowsToProtectInvariant()
    {
        // A warehouse must always serve ≥ 1 mode. Disabling the last
        // one would leave it in a useless state — caller should
        // Deactivate instead, which expresses the intent ("offline")
        // and preserves data integrity.
        var warehouse = NewBangkok();

        var act = () => warehouse.DisableServiceMode(TransportMode.Amr);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only enabled service mode*Deactivate*");
    }

    [Fact]
    public void DisableServiceMode_OneOfMany_Removes()
    {
        var warehouse = Warehouse.Create(
            "WH-BKK-01", "Bangkok DC",
            new LatLng(13.7, 100.5),
            new Address("123 Sukhumvit Rd"),
            new[] { TransportMode.Amr, TransportMode.Manual });

        warehouse.DisableServiceMode(TransportMode.Amr);

        warehouse.ServesMode(TransportMode.Amr).Should().BeFalse();
        warehouse.ServesMode(TransportMode.Manual).Should().BeTrue();
    }

    // ─── Geofence ────────────────────────────────────────────────────────

    [Fact]
    public void SetGeofenceRadius_Positive_StoresAndClearsPolygon()
    {
        var warehouse = NewBangkok();
        warehouse.SetGeofencePolygon("POLYGON((100 13, 101 13, 101 14, 100 14, 100 13))");

        warehouse.SetGeofenceRadius(100);

        warehouse.GeofenceRadiusM.Should().Be(100);
        warehouse.GeofenceAreaWkt.Should().BeNull("setting radius must clear polygon — mutually exclusive");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void SetGeofenceRadius_NonPositive_Throws(int radius)
    {
        var warehouse = NewBangkok();

        var act = () => warehouse.SetGeofenceRadius(radius);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetGeofenceRadius_Null_ClearsBothGeofenceFields()
    {
        var warehouse = NewBangkok();
        warehouse.SetGeofenceRadius(100);

        warehouse.SetGeofenceRadius(null);

        warehouse.GeofenceRadiusM.Should().BeNull();
        warehouse.GeofenceAreaWkt.Should().BeNull();
    }

    [Fact]
    public void SetGeofencePolygon_ValidWkt_StoresAndClearsRadius()
    {
        var warehouse = NewBangkok();
        warehouse.SetGeofenceRadius(100);
        var wkt = "POLYGON((100.5 13.7, 100.51 13.7, 100.51 13.71, 100.5 13.71, 100.5 13.7))";

        warehouse.SetGeofencePolygon(wkt);

        warehouse.GeofenceAreaWkt.Should().Be(wkt);
        warehouse.GeofenceRadiusM.Should().BeNull("setting polygon must clear radius — mutually exclusive");
    }

    [Fact]
    public void SetGeofencePolygon_NonPolygonWkt_Throws()
    {
        // Other WKT geometry types (POINT, LINESTRING, MULTIPOLYGON) aren't
        // supported by the Phase 4 geofence validator yet. Reject early
        // instead of silently accepting and failing at runtime.
        var warehouse = NewBangkok();

        var act = () => warehouse.SetGeofencePolygon("POINT(100.5 13.7)");

        act.Should().Throw<ArgumentException>().WithMessage("*POLYGON*");
    }

    // ─── Activation lifecycle ────────────────────────────────────────────

    [Fact]
    public void Deactivate_ActiveWarehouse_Transitions()
    {
        var warehouse = NewBangkok();

        warehouse.Deactivate();

        warehouse.IsActive.Should().BeFalse();
        warehouse.DomainEvents.OfType<WarehouseDeactivatedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Deactivate_AlreadyInactive_IsIdempotent()
    {
        var warehouse = NewBangkok();
        warehouse.Deactivate();

        warehouse.Deactivate();

        warehouse.DomainEvents.OfType<WarehouseDeactivatedDomainEvent>()
            .Should().ContainSingle("second Deactivate call should not raise duplicate event");
    }

    [Fact]
    public void IsAvailableAt_InactiveWarehouse_ReturnsFalse_EvenWithinHours()
    {
        var warehouse = NewBangkok();
        warehouse.Deactivate();

        warehouse.IsAvailableAt(new DateTime(2026, 6, 23, 10, 0, 0)).Should().BeFalse();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static Warehouse NewBangkok() => Warehouse.Create(
        "WH-BKK-01",
        "Bangkok DC",
        new LatLng(13.7, 100.5),
        new Address("123 Sukhumvit Rd", city: "Bangkok"),
        new[] { TransportMode.Amr });
}

// Smaller VO-level tests — focused on edge cases that would surface
// in production data (out-of-range coordinates, blank strings, etc.)
public class LatLngTests
{
    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void Create_OutOfRange_Throws(double lat, double lng)
    {
        var act = () => new LatLng(lat, lng);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    [InlineData(13.7, 100.5)]
    public void Create_Boundary_Accepted(double lat, double lng)
    {
        var coord = new LatLng(lat, lng);
        coord.Lat.Should().Be(lat);
        coord.Lng.Should().Be(lng);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        new LatLng(13.7, 100.5).Should().Be(new LatLng(13.7, 100.5));
    }
}

public class OperatingHoursTests
{
    [Fact]
    public void AlwaysOpen_IsOpenEveryDay_EveryHour()
    {
        var hours = OperatingHours.AlwaysOpen();

        // Sample across week + day
        for (int day = 0; day < 7; day++)
        {
            var sunday = new DateTime(2026, 6, 21);   // Sunday
            var date = sunday.AddDays(day);
            hours.IsOpenAt(date.AddHours(3)).Should().BeTrue();
            hours.IsOpenAt(date.AddHours(12)).Should().BeTrue();
            hours.IsOpenAt(date.AddHours(23)).Should().BeTrue();
        }
    }

    [Fact]
    public void Standard_WeekdayOnly_RejectsWeekends()
    {
        var hours = OperatingHours.Standard(
            weekdayOpen: new TimeSpan(8, 0, 0),
            weekdayClose: new TimeSpan(18, 0, 0));

        // Monday at 10am — open
        hours.IsOpenAt(new DateTime(2026, 6, 22, 10, 0, 0)).Should().BeTrue();
        // Monday at 19:00 — closed (after hours)
        hours.IsOpenAt(new DateTime(2026, 6, 22, 19, 0, 0)).Should().BeFalse();
        // Sunday at 10am — closed (weekend)
        hours.IsOpenAt(new DateTime(2026, 6, 21, 10, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void Standard_InvertedTimes_Throws()
    {
        // Close-before-open is meaningless and would silently make the
        // warehouse "always closed". Reject at construction.
        var act = () => OperatingHours.Standard(
            weekdayOpen: new TimeSpan(18, 0, 0),
            weekdayClose: new TimeSpan(8, 0, 0));

        act.Should().Throw<ArgumentException>();
    }
}
