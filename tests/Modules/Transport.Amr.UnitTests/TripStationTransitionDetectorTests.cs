using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Facility.Application.Services;
using DTMS.SharedKernel.Diagnostics;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Options;
using DTMS.Transport.Amr.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace VendorAdapter.UnitTests;

// P1/P2: shared pickup/drop detection. Fires the Trip signal when a mission
// FINISHES at the pickup/drop station, exactly once (fire-once guard), from
// either the webhook or the reconciler.
public class TripStationTransitionDetectorTests
{
    private const int PickupVendorId = 159;
    private const int DropVendorId = 151;

    private static (Trip trip, Guid pickup, Guid drop) InProgressTripWithStations()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", pickup, drop);
        trip.MarkVendorStarted();   // Created → InProgress
        return (trip, pickup, drop);
    }

    private static IFacilityReadService Facility(Guid pickup, Guid drop)
    {
        var f = Substitute.For<IFacilityReadService>();
        f.ResolveStationByVendorRefAsync(PickupVendorId.ToString(), Arg.Any<CancellationToken>()).Returns(pickup);
        f.ResolveStationByVendorRefAsync(DropVendorId.ToString(), Arg.Any<CancellationToken>()).Returns(drop);
        return f;
    }

    [Fact]
    public async Task MoveFinishedAtPickupStation_FiresPickupOnce()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();

        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", PickupVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorPickedUpAt.Should().NotBeNull();
        trip.Events.Count(e => e.EventType == "VendorPickupCompleted").Should().Be(1);

        // Second observation of the same signal is a no-op (fire-once guard).
        var again = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", PickupVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);
        again.Should().BeFalse();
        trip.Events.Count(e => e.EventType == "VendorPickupCompleted").Should().Be(1);
    }

    [Fact]
    public async Task MoveFinishedAtDropStation_AfterPickup_FiresDropWithPodPolicy()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        trip.MarkVendorPickedUp();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorDroppedAt.Should().NotBeNull();
        trip.Events.Count(e => e.EventType == "VendorDropCompleted").Should().Be(1);
    }

    // Trip 6435 regression (2026-08-05): round-trip FG templates visit the drop
    // station FIRST to fetch the empty rack. That visit must not fire the drop
    // — OMS rejects a droppedoff callback before pickup, and the fire-once
    // guard would then swallow the real drop visit.
    [Fact]
    public async Task MoveFinishedAtDropStation_BeforePickup_DoesNotFire()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        // Rack-fetch visit: MOVE finishes at the drop station before any pickup.
        var fetchVisit = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        fetchVisit.Should().BeFalse();
        trip.VendorDroppedAt.Should().BeNull();

        // Pickup happens, then the robot returns to the drop station — the
        // real drop visit fires normally.
        var pickedUp = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", PickupVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);
        pickedUp.Should().BeTrue();

        var realDrop = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        realDrop.Should().BeTrue();
        trip.VendorDroppedAt.Should().NotBeNull();
        trip.Events.Count(e => e.EventType == "VendorDropCompleted").Should().Be(1);
    }

    // Trip 6511 regression (2026-08-06): the pickup webhook lands first, THEN
    // the reconciler replays the whole mission list — including the long-
    // finished rack-fetch visit. "Has pickup fired?" alone lets that replay
    // through, which stamped the fetch time (07:17:34) as the drop instead of
    // the real arrival (07:27:50) and notified OMS before the goods landed.
    [Fact]
    public async Task MoveFinishedAtDropStation_ReplayedFetchVisitAfterPickup_DoesNotFire()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        // Pickup arrived via webhook, with sub-second precision.
        var pickedUpAt = new DateTime(2026, 8, 6, 7, 24, 49, 310, DateTimeKind.Utc);
        trip.MarkVendorPickedUp(actedAt: pickedUpAt);

        // Reconciler now replays the rack-fetch mission that finished minutes
        // BEFORE the pickup (whole-second precision, as RIOT3 reports it).
        var fetchVisitAt = new DateTime(2026, 8, 6, 7, 17, 34, DateTimeKind.Utc);
        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: fetchVisitAt,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeFalse();
        trip.VendorDroppedAt.Should().BeNull();

        // The real drop, minutes after the pickup, still fires — and carries
        // its own timestamp, not the fetch visit's.
        var realDropAt = new DateTime(2026, 8, 6, 7, 27, 50, DateTimeKind.Utc);
        var realDrop = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: realDropAt,
            NullLogger.Instance, CancellationToken.None);

        realDrop.Should().BeTrue();
        trip.VendorDroppedAt.Should().Be(realDropAt);
    }

    // The reconciler reads whole seconds while the webhook reads sub-seconds,
    // so a drop can legitimately look marginally EARLIER than its pickup. That
    // must not be mistaken for a replayed fetch visit — being skipped would be
    // permanent, since every later poll re-reads the same truncated value.
    [Fact]
    public async Task MoveFinishedAtDropStation_SubSecondClockSkew_StillFires()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        trip.MarkVendorPickedUp(actedAt: new DateTime(2026, 8, 6, 7, 24, 49, 310, DateTimeKind.Utc));

        // Truncated to the second, this reads 310ms before the pickup.
        var truncatedDropAt = new DateTime(2026, 8, 6, 7, 24, 49, DateTimeKind.Utc);
        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: truncatedDropAt,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorDroppedAt.Should().Be(truncatedDropAt);
    }

    // Webhook paths that carry no timestamp cannot be ordered chronologically —
    // they must still fall back to the "pickup has fired" guard alone.
    [Fact]
    public async Task MoveFinishedAtDropStation_NoActedAtAfterPickup_StillFires()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);
        trip.MarkVendorPickedUp();

        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorDroppedAt.Should().NotBeNull();
    }

    // Legacy trips persisted before retry support carry no PickupStationId —
    // pickup can never fire on them, so the ordering gate must not apply.
    [Fact]
    public async Task MoveFinishedAtDropStation_NoPickupLeg_StillFires()
    {
        var drop = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(
            Guid.NewGuid(), "upper-G1", "ORD-1", pickupStationId: null, dropStationId: drop);
        trip.MarkVendorStarted();

        var facility = Substitute.For<IFacilityReadService>();
        facility.ResolveStationByVendorRefAsync(DropVendorId.ToString(), Arg.Any<CancellationToken>())
            .Returns(drop);
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, "MOVE", "FINISHED", DropVendorId,
            facility, orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorDroppedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("MOVE", "PROCESSING", PickupVendorId)]   // not finished
    [InlineData("WAIT", "FINISHED", PickupVendorId)]     // not MOVE
    [InlineData("ACT", "FINISHED", DropVendorId)]        // ACT station is a stale lagging dock — never a signal
    [InlineData("ACT", "FINISHED", PickupVendorId)]      // same for pickup side
    [InlineData("MOVE", "FINISHED", 0)]                   // no station
    [InlineData("MOVE", "FINISHED", 999)]                // resolves to neither pickup nor drop
    public async Task NonQualifyingMission_DoesNotFire(string type, string state, int vendorStationId)
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();

        var fired = await TripStationTransitionDetector.TryApplyAsync(
            trip, type, state, vendorStationId == 0 ? (int?)null : vendorStationId,
            Facility(pickup, drop), orderReader, actedAt: null,
            NullLogger.Instance, CancellationToken.None);

        fired.Should().BeFalse();
        trip.VendorPickedUpAt.Should().BeNull();
        trip.VendorDroppedAt.Should().BeNull();
    }
}

// Reconciler safety net: DetectStationTransitionsAsync runs the same detection
// over the order-query missions when the webhook was dropped.
public class Riot3ReconciliationStationDetectionTests
{
    private static Riot3ReconciliationService BuildService() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IOptionsMonitor<ReconciliationOptions>>(),
            NullLogger<Riot3ReconciliationService>.Instance,
            new WorkflowMetrics());

    [Fact]
    public async Task DetectStationTransitions_FiresPickupFromOrderQueryMission_WhenWebhookMissed()
    {
        var pickup = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", pickup, Guid.NewGuid());
        trip.MarkVendorStarted();

        var facility = Substitute.For<IFacilityReadService>();
        facility.ResolveStationByVendorRefAsync("159", Arg.Any<CancellationToken>()).Returns(pickup);
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();

        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                new()
                {
                    MissionKey = "m0", MissionIndex = 0, Type = "MOVE", State = "FINISHED",
                    StationId = 159, FinishedTime = "2026-07-09T08:33:18Z",
                },
            },
        };

        var fired = await BuildService().DetectStationTransitionsAsync(
            trip, data, facility, orderReader, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorPickedUpAt.Should().Be(new DateTime(2026, 7, 9, 8, 33, 18, DateTimeKind.Utc));
    }

    // Round-trip heal: when every webhook was dropped, the reconciler replays
    // the full mission list in template order. The rack-fetch visit to the drop
    // station (before pickup) must be skipped; drop must stamp the FINAL visit's
    // time, not the fetch visit's.
    [Fact]
    public async Task DetectStationTransitions_RoundTripTemplate_DropUsesFinalVisitNotRackFetch()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", pickup, drop);
        trip.MarkVendorStarted();

        var facility = Substitute.For<IFacilityReadService>();
        facility.ResolveStationByVendorRefAsync("159", Arg.Any<CancellationToken>()).Returns(pickup);
        facility.ResolveStationByVendorRefAsync("151", Arg.Any<CancellationToken>()).Returns(drop);
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                // Rack fetch at the drop station, then pickup, then real drop.
                new()
                {
                    MissionKey = "m0", MissionIndex = 0, Type = "MOVE", State = "FINISHED",
                    StationId = 151, FinishedTime = "2026-08-05T10:56:57Z",
                },
                new()
                {
                    MissionKey = "m1", MissionIndex = 1, Type = "MOVE", State = "FINISHED",
                    StationId = 159, FinishedTime = "2026-08-05T11:02:29Z",
                },
                new()
                {
                    MissionKey = "m2", MissionIndex = 2, Type = "MOVE", State = "FINISHED",
                    StationId = 151, FinishedTime = "2026-08-05T11:06:01Z",
                },
            },
        };

        var fired = await BuildService().DetectStationTransitionsAsync(
            trip, data, facility, orderReader, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorPickedUpAt.Should().Be(new DateTime(2026, 8, 5, 11, 2, 29, DateTimeKind.Utc));
        trip.VendorDroppedAt.Should().Be(new DateTime(2026, 8, 5, 11, 6, 1, DateTimeKind.Utc));
    }

    // Trip 6511 end-to-end (2026-08-06): the pickup webhook already landed, so
    // the reconciler's replay of the earlier rack-fetch mission is the only
    // thing that can still fire the drop. It must pick the FINAL visit.
    [Fact]
    public async Task DetectStationTransitions_ReplayAfterPickupWebhook_SkipsFetchVisit()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", pickup, drop);
        trip.MarkVendorStarted();
        // Webhook already recorded pickup, sub-second precision.
        trip.MarkVendorPickedUp(actedAt: new DateTime(2026, 8, 6, 7, 24, 49, 310, DateTimeKind.Utc));

        var facility = Substitute.For<IFacilityReadService>();
        facility.ResolveStationByVendorRefAsync("159", Arg.Any<CancellationToken>()).Returns(pickup);
        facility.ResolveStationByVendorRefAsync("151", Arg.Any<CancellationToken>()).Returns(drop);
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        orderReader.GetRequiresDropPodAsync(trip.DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns((bool?)false);

        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                new()
                {
                    MissionKey = "m0", MissionIndex = 0, Type = "MOVE", State = "FINISHED",
                    StationId = 151, FinishedTime = "2026-08-06T07:17:34Z",   // rack fetch
                },
                new()
                {
                    MissionKey = "m1", MissionIndex = 1, Type = "MOVE", State = "FINISHED",
                    StationId = 159, FinishedTime = "2026-08-06T07:24:49Z",   // pickup (already fired)
                },
                new()
                {
                    MissionKey = "m2", MissionIndex = 2, Type = "MOVE", State = "FINISHED",
                    StationId = 151, FinishedTime = "2026-08-06T07:27:50Z",   // real drop
                },
            },
        };

        var fired = await BuildService().DetectStationTransitionsAsync(
            trip, data, facility, orderReader, CancellationToken.None);

        fired.Should().BeTrue();
        trip.VendorDroppedAt.Should().Be(new DateTime(2026, 8, 6, 7, 27, 50, DateTimeKind.Utc),
            "the drop must carry the final visit's time, not the rack fetch's");
    }

    [Fact]
    public async Task DetectStationTransitions_SkipsAllLookups_WhenBothAlreadyFired()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", pickup, drop);
        trip.MarkVendorStarted();
        trip.MarkVendorPickedUp();
        trip.MarkVendorDropCompleted(requiresDropPod: false);

        var facility = Substitute.For<IFacilityReadService>();
        var orderReader = Substitute.For<IDeliveryOrderStatusReader>();
        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                new() { MissionKey = "m", Type = "MOVE", State = "FINISHED", StationId = 159 },
            },
        };

        var fired = await BuildService().DetectStationTransitionsAsync(
            trip, data, facility, orderReader, CancellationToken.None);

        fired.Should().BeFalse();
        await facility.DidNotReceive().ResolveStationByVendorRefAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
