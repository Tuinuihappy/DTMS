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
    public async Task MoveFinishedAtDropStation_FiresDropWithPodPolicy()
    {
        var (trip, pickup, drop) = InProgressTripWithStations();
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
