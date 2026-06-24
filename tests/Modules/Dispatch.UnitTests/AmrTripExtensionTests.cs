using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using FluentAssertions;

namespace Dispatch.UnitTests;

// Phase 3b — locks down the AmrTripExtension entity contract:
//   - factory rejects empty TripId
//   - first-write-wins on VendorOrderKey / VendorVehicleKey / VendorVehicleName
//     (the AMR webhooks fire multiple times for the same trip — a
//      late vehicle key from a retry must not overwrite the first one)
//   - SetPauseSource / ClearPauseSource toggle correctly
//
// These behaviours used to live on the Trip aggregate properties;
// migrating them to the extension table preserves them only if the
// methods themselves keep the semantics. These tests pin that.
public class AmrTripExtensionTests
{
    [Fact]
    public void Create_RejectsEmptyTripId()
    {
        var act = () => AmrTripExtension.Create(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("tripId");
    }

    [Fact]
    public void Create_Returns_ExtensionBoundToTrip()
    {
        var tripId = Guid.NewGuid();

        var ext = AmrTripExtension.Create(tripId);

        ext.TripId.Should().Be(tripId);
        ext.VendorOrderKey.Should().BeNull();
        ext.VendorVehicleKey.Should().BeNull();
        ext.VendorVehicleName.Should().BeNull();
        ext.VendorPauseSource.Should().BeNull();
    }

    [Fact]
    public void AttachVendorOrder_FirstWriteWins()
    {
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVendorOrder("RIOT3-FIRST");
        ext.AttachVendorOrder("RIOT3-SECOND");   // late-arriving vendor retry

        ext.VendorOrderKey.Should().Be("RIOT3-FIRST");
    }

    [Fact]
    public void AttachVendorOrder_TrimsWhitespace()
    {
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVendorOrder("  RIOT3-KEY  ");

        ext.VendorOrderKey.Should().Be("RIOT3-KEY");
    }

    [Fact]
    public void AttachVendorOrder_IgnoresEmptyOrWhitespace()
    {
        // Defensive: vendor occasionally returns empty string. Treat as "no key".
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVendorOrder("");
        ext.AttachVendorOrder("   ");

        ext.VendorOrderKey.Should().BeNull();
    }

    [Fact]
    public void AttachVehicle_FirstWriteWins_PerField()
    {
        // Key and Name are independent — a later webhook may bring a Name
        // for a vehicle whose Key arrived earlier. Each field locks
        // independently, so a key-only first webhook still permits a
        // name-only second webhook to fill in the missing field.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVehicle("Delta6FAN1", vendorVehicleName: null);
        ext.AttachVehicle("LATER-KEY", "FAN1_STANDARD_NO5");

        ext.VendorVehicleKey.Should().Be("Delta6FAN1");       // first-write-wins
        ext.VendorVehicleName.Should().Be("FAN1_STANDARD_NO5"); // late-fill on null field
    }

    [Fact]
    public void AttachVehicle_NullInputs_LeaveFieldsUnchanged()
    {
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVehicle(null, null);

        ext.VendorVehicleKey.Should().BeNull();
        ext.VendorVehicleName.Should().BeNull();
    }

    [Fact]
    public void SetPauseSource_ThenClear_RoundTrips()
    {
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.SetPauseSource(VendorPauseSource.Held);
        ext.VendorPauseSource.Should().Be(VendorPauseSource.Held);

        ext.ClearPauseSource();
        ext.VendorPauseSource.Should().BeNull();
    }

    [Fact]
    public void SetPauseSource_Overwrites_PreviousSource()
    {
        // Unlike VendorOrderKey / VendorVehicleKey, the pause source is
        // not first-write-wins — a Vendor-driven pause may transition to
        // an Operator-driven hold without an intervening Resume. The
        // current source replaces whatever was there.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.SetPauseSource(VendorPauseSource.Hang);
        ext.SetPauseSource(VendorPauseSource.Held);

        ext.VendorPauseSource.Should().Be(VendorPauseSource.Held);
    }
}
