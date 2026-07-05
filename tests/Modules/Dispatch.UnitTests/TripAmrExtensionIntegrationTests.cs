using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Events;
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

    // ── BackfillVendorVehicle (terminal-state robot recovery) ────────────
    // The fix for "trip completed but shows no vehicle": TASK_PROCESSING was
    // missed, so the robot is recovered from RIOT3's terminal record after
    // the trip is already Completed/Failed.

    [Fact]
    public void BackfillVendorVehicle_OnTerminalTrip_WithNoVehicle_FillsIt()
    {
        // Core scenario. Trip reached Completed WITHOUT ever capturing a
        // robot. Backfill fills it — status stays terminal, history + cache
        // populated, returns true.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();            // Created → InProgress, no vehicle
        trip.MarkVendorCompleted();          // → Completed
        trip.VendorVehicleKey.Should().BeNull();

        var wrote = trip.BackfillVendorVehicle("e47366d4", "FAN1_STANDARD_NO5", "reconciler-terminal");

        wrote.Should().BeTrue();
        trip.Status.Should().Be(TripStatus.Completed);          // status untouched
        trip.VendorVehicleKey.Should().Be("e47366d4");
        trip.VendorVehicleName.Should().Be("FAN1_STANDARD_NO5");
        trip.AmrExtension!.VehicleAssignments.Should().HaveCount(1);
        trip.AmrExtension.VehicleAssignments[0].Source.Should().Be("reconciler-terminal");
    }

    [Fact]
    public void BackfillVendorVehicle_ContrastsWith_ReconcileVehicleAssignment_OnTerminal()
    {
        // Locks the distinction that motivated a separate method: on a
        // terminal trip ReconcileVehicleAssignment is a no-op (guards out),
        // while BackfillVendorVehicle succeeds.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();
        trip.MarkVendorCompleted();

        trip.ReconcileVehicleAssignment("e47366d4", "FAN1_NO5", "reconciler").Should().BeFalse();
        trip.VendorVehicleKey.Should().BeNull();                // reconcile refused terminal

        trip.BackfillVendorVehicle("e47366d4", "FAN1_NO5", "reconciler-terminal").Should().BeTrue();
        trip.VendorVehicleKey.Should().Be("e47366d4");
    }

    [Fact]
    public void BackfillVendorVehicle_WhenVehicleAlreadyCaptured_IsNoOp_PrimaryWins()
    {
        // The live capture path always wins — backfill must never clobber a
        // robot already recorded, nor grow the history.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1", vendorVehicleName: "FAN1_NO5");
        trip.MarkVendorCompleted();

        var wrote = trip.BackfillVendorVehicle("DIFFERENT-KEY", "OTHER", "reconciler-terminal");

        wrote.Should().BeFalse();
        trip.VendorVehicleKey.Should().Be("Delta6FAN1");        // unchanged
        trip.AmrExtension!.VehicleAssignments.Should().HaveCount(1);
    }

    [Fact]
    public void BackfillVendorVehicle_NullKey_IsNoOp_NoEvent()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();
        trip.MarkVendorCompleted();

        trip.BackfillVendorVehicle(null, "name-only", "reconciler-terminal").Should().BeFalse();

        trip.VendorVehicleKey.Should().BeNull();
        trip.Events.Should().NotContain(e => e.EventType == "VehicleBackfilled");
        trip.DomainEvents.Should().NotContain(e => e is TripVehicleBackfilledDomainEvent);
    }

    [Fact]
    public void BackfillVendorVehicle_FiresBackfilledDomainEvent_ForBiSync()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();
        trip.MarkVendorCompleted();

        trip.BackfillVendorVehicle("e47366d4", "FAN1_NO5", "reconciler-terminal");

        trip.DomainEvents.Should().ContainSingle(e => e is TripVehicleBackfilledDomainEvent);
        var evt = (TripVehicleBackfilledDomainEvent)trip.DomainEvents.Single(e => e is TripVehicleBackfilledDomainEvent);
        evt.VendorVehicleKey.Should().Be("e47366d4");
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
