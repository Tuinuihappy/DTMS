using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using FluentAssertions;

namespace Dispatch.UnitTests;

// Phase 3b/3d — locks down the AmrTripExtension entity contract:
//   - factory rejects empty TripId
//   - VendorOrderKey is first-write-wins (RIOT3 orderKey never changes)
//   - VendorVehicleKey is last-write-wins via RecordVehicleAssignment
//     (Phase 3d Bug #2 fix — replaces the old first-write-wins
//     AttachVehicle that silently dropped real vendor reassignments)
//   - History append-only + idempotent (duplicate webhook = no-op)
//   - SetPauseSource / ClearPauseSource toggle correctly
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
        ext.VehicleAssignments.Should().BeEmpty();
    }

    [Fact]
    public void AttachVendorOrder_FirstWriteWins()
    {
        // VendorOrderKey IS first-write-wins (RIOT3 orderKey never changes
        // for a given trip — unlike vehicleKey which can legitimately
        // reassign). The dispatcher writes it at create time; webhooks
        // never overwrite it.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVendorOrder("RIOT3-FIRST");
        ext.AttachVendorOrder("RIOT3-SECOND");

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
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.AttachVendorOrder("");
        ext.AttachVendorOrder("   ");

        ext.VendorOrderKey.Should().BeNull();
    }

    [Fact]
    public void RecordVehicleAssignment_FirstCall_PopulatesCacheAndHistory()
    {
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.RecordVehicleAssignment("Delta6FAN1", "FAN1_STANDARD_NO5", source: "TASK_PROCESSING");

        ext.VendorVehicleKey.Should().Be("Delta6FAN1");
        ext.VendorVehicleName.Should().Be("FAN1_STANDARD_NO5");
        ext.VehicleAssignments.Should().HaveCount(1);
        ext.VehicleAssignments[0].Sequence.Should().Be(1);
        ext.VehicleAssignments[0].VendorVehicleKey.Should().Be("Delta6FAN1");
        ext.VehicleAssignments[0].Source.Should().Be("TASK_PROCESSING");
    }

    [Fact]
    public void RecordVehicleAssignment_DuplicatePayload_IsIdempotent_NoNewHistoryRow()
    {
        // RIOT3 fires duplicate TASK_PROCESSING webhooks routinely (retries,
        // mission-state catchup). The entity must not pollute its history
        // table when the same (key, name) arrives twice — only genuine
        // reassignments grow the audit trail.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.RecordVehicleAssignment("Delta6FAN1", "FAN1_NO5", "TASK_PROCESSING");
        ext.RecordVehicleAssignment("Delta6FAN1", "FAN1_NO5", "TASK_PROCESSING");
        ext.RecordVehicleAssignment("Delta6FAN1", "FAN1_NO5", "TASK_PROCESSING");

        ext.VehicleAssignments.Should().HaveCount(1);
        ext.VendorVehicleKey.Should().Be("Delta6FAN1");
    }

    [Fact]
    public void RecordVehicleAssignment_RealReassignment_AppendsHistory_AndUpdatesCache()
    {
        // The scenario that motivated Phase 3d Bug #2: RIOT3 reassigns
        // robot mid-trip (robot A fails / battery dead → robot B takes
        // over). The history captures both assignments in order; the
        // cache pointer (used by PASS / CANCEL / PAUSE commands) flips
        // to the latest robot so subsequent commands target the right one.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.RecordVehicleAssignment("Delta6FAN1", "FAN1_NO5", "TASK_PROCESSING");
        ext.RecordVehicleAssignment("Delta7BFAN2", "FAN2_NO3", "TASK_PROCESSING");

        ext.VehicleAssignments.Should().HaveCount(2);
        ext.VehicleAssignments[0].Sequence.Should().Be(1);
        ext.VehicleAssignments[0].VendorVehicleKey.Should().Be("Delta6FAN1");
        ext.VehicleAssignments[1].Sequence.Should().Be(2);
        ext.VehicleAssignments[1].VendorVehicleKey.Should().Be("Delta7BFAN2");
        // Cache pointer reflects the most recent assignment.
        ext.VendorVehicleKey.Should().Be("Delta7BFAN2");
        ext.VendorVehicleName.Should().Be("FAN2_NO3");
    }

    [Fact]
    public void RecordVehicleAssignment_NullOrEmptyKey_IsNoOp()
    {
        // Defensive: a webhook with no vehicleKey shouldn't grow history
        // (it carries no new information). Same as the old AttachVehicle
        // behaviour for backward compat with smoke runs that probe edges.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.RecordVehicleAssignment(null, "name-only", "TASK_PROCESSING");
        ext.RecordVehicleAssignment("", "name-only", "TASK_PROCESSING");
        ext.RecordVehicleAssignment("   ", "name-only", "TASK_PROCESSING");

        ext.VehicleAssignments.Should().BeEmpty();
        ext.VendorVehicleKey.Should().BeNull();
    }

    [Fact]
    public void RecordVehicleAssignment_NameOnlyDiff_IsTreatedAsReassignment()
    {
        // Webhook 1: key+name. Webhook 2: same key, name updated (RIOT3
        // sometimes resolves robot name after the key in a later
        // notification). The differing name counts as a new assignment —
        // operators see the name appear in audit at the moment RIOT3
        // resolved it, instead of silently flipping under their feet.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.RecordVehicleAssignment("Delta6FAN1", null, "TASK_PROCESSING");
        ext.RecordVehicleAssignment("Delta6FAN1", "FAN1_NO5", "TASK_PROCESSING");

        ext.VehicleAssignments.Should().HaveCount(2);
        ext.VendorVehicleName.Should().Be("FAN1_NO5");
    }

    [Fact]
    public void RecordVehicleAssignment_RejectsEmptySource()
    {
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        var act = () => ext.RecordVehicleAssignment("Delta6FAN1", null, source: "");

        act.Should().Throw<ArgumentException>().WithParameterName("source");
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
        // Unlike VendorOrderKey (first-write-wins), the pause source is
        // not first-write-wins — a Vendor-driven hang may transition to
        // an Operator-driven hold without an intervening Resume.
        var ext = AmrTripExtension.Create(Guid.NewGuid());

        ext.SetPauseSource(VendorPauseSource.Hang);
        ext.SetPauseSource(VendorPauseSource.Held);

        ext.VendorPauseSource.Should().Be(VendorPauseSource.Held);
    }
}
