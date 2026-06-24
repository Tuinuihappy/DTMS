using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using FluentAssertions;

namespace Dispatch.UnitTests;

// Phase 3b — locks down the Trip ↔ AmrTripExtension wiring:
//   - Trip.CreateForEnvelope creates the extension iff vendorOrderKey supplied
//     (so Manual / Fleet trips with vendorOrderKey=null get no extension row)
//   - Trip.MarkVendorStarted creates the extension lazily on first AMR webhook
//   - Trip.Pause creates the extension lazily for non-AMR-origin trips so the
//     pause source still persists
//   - Trip.Resume clears the source via the extension
//   - Delegating properties (Trip.VendorOrderKey etc.) return AmrExtension's
//     values when present, null when AmrExtension is null
//   - Trip.AcknowledgeRobotPass reads the vehicle key off the extension
//
// These cover the "looks-like-before, works-different-underneath" contract
// that lets the 25+ consumer call sites (handlers / projectors / queries)
// keep using `trip.VendorOrderKey` syntax unchanged.
public class TripAmrExtensionIntegrationTests
{
    [Fact]
    public void CreateForEnvelope_WithVendorOrderKey_CreatesExtension()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");

        trip.AmrExtension.Should().NotBeNull();
        trip.AmrExtension!.VendorOrderKey.Should().Be("RIOT3-ABC");
        trip.VendorOrderKey.Should().Be("RIOT3-ABC");   // delegated read
    }

    [Fact]
    public void CreateForEnvelope_NullVendorOrderKey_LeavesExtensionUnset()
    {
        // RIOT3 occasionally returns 200 with no orderKey; Manual / Fleet
        // trips never have one. Either way: no extension row should be
        // created — the schema invariant is "AMR-only have an extension".
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", vendorOrderKey: null);

        trip.AmrExtension.Should().BeNull();
        trip.VendorOrderKey.Should().BeNull();   // delegated read on null nav
    }

    [Fact]
    public void MarkVendorStarted_WithVehicleKey_CreatesExtensionLazily()
    {
        // CreateForEnvelope was called with null vendorOrderKey → no extension.
        // First VendorStarted webhook brings a vehicle key → extension created on demand.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", null);

        trip.MarkVendorStarted(vehicleId: null, vendorVehicleKey: "Delta6FAN1", vendorVehicleName: "FAN1_NO5");

        trip.AmrExtension.Should().NotBeNull();
        trip.AmrExtension!.VendorVehicleKey.Should().Be("Delta6FAN1");
        trip.AmrExtension.VendorVehicleName.Should().Be("FAN1_NO5");
        trip.VendorVehicleKey.Should().Be("Delta6FAN1");   // delegated
        trip.VendorVehicleName.Should().Be("FAN1_NO5");
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public void MarkVendorStarted_NoVehicleKey_DoesNotCreateEmptyExtension()
    {
        // Defensive: a webhook for a Manual / Fleet trip would not carry
        // a vehicle key. We must not create an empty extension row — that
        // breaks the "AMR-only have an extension" invariant.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", null);

        trip.MarkVendorStarted(vehicleId: null, vendorVehicleKey: null, vendorVehicleName: null);

        trip.AmrExtension.Should().BeNull();
        trip.Status.Should().Be(TripStatus.InProgress);   // status still advances
    }

    [Fact]
    public void Pause_CreatesExtension_AndSetsSource()
    {
        // Even for a trip created with no vendor key (Manual / Fleet
        // shape), a Pause must persist its source. The lazy-creation
        // pattern means Manual / Fleet eventually grow an AmrExtension
        // row — that's OK; Phase 4 will introduce a ManualTripExtension
        // and the Manual pause flow will write there instead.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", null);
        trip.MarkVendorStarted();   // Created → InProgress

        trip.Pause(VendorPauseSource.Hang);

        trip.AmrExtension.Should().NotBeNull();
        trip.AmrExtension!.VendorPauseSource.Should().Be(VendorPauseSource.Hang);
        trip.VendorPauseSource.Should().Be(VendorPauseSource.Hang);  // delegated
        trip.Status.Should().Be(TripStatus.Paused);
    }

    [Fact]
    public void Resume_ClearsPauseSource_OnExtension()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();
        trip.Pause(VendorPauseSource.Held);

        trip.Resume();

        trip.VendorPauseSource.Should().BeNull();
        trip.AmrExtension!.VendorPauseSource.Should().BeNull();
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public void AcknowledgeRobotPass_RequiresVehicleKey_OnExtension()
    {
        // PASS is RIOT3-specific (routed by deviceKey). Without an extension
        // carrying a vehicle key, the command must throw — same contract
        // as the pre-3b "no vendor vehicle key on file" guard.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();   // no vehicle key on this MarkVendorStarted call

        var act = () => trip.AcknowledgeRobotPass();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no vendor vehicle key*");
    }

    [Fact]
    public void AcknowledgeRobotPass_Succeeds_When_ExtensionCarriesVehicleKey()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1");

        trip.AcknowledgeRobotPass();   // no throw

        trip.Events.Should().Contain(e => e.EventType == "RobotPassAcknowledged");
    }

    [Fact]
    public void DelegatingProperties_AllReturnNull_When_NoExtension()
    {
        // Sanity check for the read-side delegation pattern. Trip core
        // exposes 4 vendor properties as expression-bodied delegations;
        // every one must short-circuit cleanly to null when the navigation
        // is unset (otherwise EF-loaded trips with no extension throw NRE).
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", null);

        trip.AmrExtension.Should().BeNull();
        trip.VendorOrderKey.Should().BeNull();
        trip.VendorVehicleKey.Should().BeNull();
        trip.VendorVehicleName.Should().BeNull();
        trip.VendorPauseSource.Should().BeNull();
    }
}
