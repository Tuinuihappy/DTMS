using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
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

// Orchestration coverage for the self-heal sweep (Finding #4). The domain
// method Trip.BackfillVendorVehicle is unit-tested in Dispatch.UnitTests; this
// pins the SERVICE contract the sweep exists for:
//   1. snapshot-FIRST — Trip.VendorFinalSnapshot is captured even when the
//      vendor record has no vehicle, so the trip drops out of the query for
//      good (no per-tick re-fetch loop).
//   2. vehicle backfilled from the terminal record when one exists.
//   3. the persisted count returned matches real backfills.
public class Riot3ReconciliationSelfHealTests
{
    private static Riot3ReconciliationService BuildService() =>
        new(
            Substitute.For<IServiceScopeFactory>(),                       // unused by the sweep
            Substitute.For<IOptionsMonitor<ReconciliationOptions>>(),    // unused by the sweep (opts passed in)
            NullLogger<Riot3ReconciliationService>.Instance,
            new WorkflowMetrics());

    private static Trip TerminalTripMissingVehicle(string upperKey = "upper-G1")
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, "RIOT3-ABC");
        trip.MarkVendorStarted();     // Created → InProgress, NO vehicle captured
        trip.MarkVendorCompleted();   // → Completed, snapshot still null
        trip.VendorVehicleKey.Should().BeNull();
        trip.VendorFinalSnapshot.Should().BeNull();
        return trip;
    }

    [Fact]
    public async Task SelfHeal_TerminalTripMissingVehicle_CapturesSnapshotAndBackfills()
    {
        var trip = TerminalTripMissingVehicle();

        var repo = Substitute.For<ITripRepository>();
        repo.GetTerminalTripsMissingVehicleAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trip> { trip });

        var query = Substitute.For<IRiot3OrderQueryService>();
        query.GetOrderByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Riot3OrderQueryData { ExecuteVehicleKey = "e47366d4", ExecuteVehicleName = "FAN1_NO5", State = "COMPLETED" });
        query.GetRawByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("{\"data\":{\"executeVehicleKey\":\"e47366d4\"}}");

        var healed = await BuildService().SelfHealMissingVehiclesAsync(
            repo, query, new ReconciliationOptions { SelfHealWindowHours = 2 }, CancellationToken.None);

        healed.Should().Be(1);
        trip.VendorVehicleKey.Should().Be("e47366d4");
        trip.VendorVehicleName.Should().Be("FAN1_NO5");
        trip.VendorFinalSnapshot.Should().NotBeNull();             // snapshot captured → drops out next tick
        trip.Status.Should().Be(TripStatus.Completed);             // status untouched
        await repo.Received(1).UpdateAsync(trip, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelfHeal_VendorRecordHasNoVehicle_SealsSnapshot_ButBackfillsNothing()
    {
        // The drop-out guarantee: even with no vehicle to recover, the snapshot
        // is sealed so the trip never re-enters the sweep. healed stays 0.
        var trip = TerminalTripMissingVehicle();

        var repo = Substitute.For<ITripRepository>();
        repo.GetTerminalTripsMissingVehicleAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trip> { trip });

        var query = Substitute.For<IRiot3OrderQueryService>();
        query.GetOrderByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Riot3OrderQueryData { State = "COMPLETED" });   // ResolvedVehicle → (null, null)
        query.GetRawByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("{\"data\":{}}");

        var healed = await BuildService().SelfHealMissingVehiclesAsync(
            repo, query, new ReconciliationOptions { SelfHealWindowHours = 2 }, CancellationToken.None);

        healed.Should().Be(0);
        trip.VendorVehicleKey.Should().BeNull();
        trip.VendorFinalSnapshot.Should().NotBeNull();             // sealed anyway → no re-fetch loop
        await repo.Received(1).UpdateAsync(trip, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelfHeal_NothingToHeal_IsNoOp()
    {
        var repo = Substitute.For<ITripRepository>();
        repo.GetTerminalTripsMissingVehicleAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trip>());
        var query = Substitute.For<IRiot3OrderQueryService>();

        var healed = await BuildService().SelfHealMissingVehiclesAsync(
            repo, query, new ReconciliationOptions(), CancellationToken.None);

        healed.Should().Be(0);
        await query.DidNotReceive().GetOrderByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
