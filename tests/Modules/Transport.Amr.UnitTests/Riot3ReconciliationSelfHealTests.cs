using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
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
    public async Task SelfHeal_ConcurrencyConflict_StopsSweep_WithoutTouchingRemainingTrips()
    {
        // xmin conflict mid-sweep = a concurrent writer (webhook / snapshot
        // consumer) won a race. The whole sweep shares one DbContext whose
        // tracker is now poisoned (failed save leaves drained OutboxMessages
        // queued as Added), so the sweep must STOP — continuing would make
        // the next trip's save re-throw and/or insert duplicate events.
        var trip1 = TerminalTripMissingVehicle("upper-G1");
        var trip2 = TerminalTripMissingVehicle("upper-G2");

        var repo = Substitute.For<ITripRepository>();
        repo.GetTerminalTripsMissingVehicleAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trip> { trip1, trip2 });
        repo.UpdateAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("xmin conflict")));

        var query = Substitute.For<IRiot3OrderQueryService>();
        query.GetOrderByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Riot3OrderQueryData { State = "COMPLETED" });
        query.GetRawByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("{\"data\":{}}");

        var healed = await BuildService().SelfHealMissingVehiclesAsync(
            repo, query, new ReconciliationOptions { SelfHealWindowHours = 2 }, CancellationToken.None);

        healed.Should().Be(0);
        // Sweep stopped at trip1 — trip2 was never fetched from RIOT3.
        await query.Received(1).GetOrderByUpperKeyAsync("upper-G1", Arg.Any<CancellationToken>());
        await query.DidNotReceive().GetOrderByUpperKeyAsync("upper-G2", Arg.Any<CancellationToken>());
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

// orderState → Transition mapping. The reconciler reads the order-level GET,
// which reports success as "SUCCEEDED" — NOT the notify's "FINISHED". Before
// the fix this state fell through to Transition.None, so any completion whose
// TASK_FINISHED webhook was lost stayed InProgress forever with no vehicle
// backfill (IsTerminalVendorState also skipped it). These pin both tokens.
public class Riot3ReconciliationStateMappingTests
{
    private static Trip InProgressTrip()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();   // Created → InProgress
        return trip;
    }

    [Theory]
    [InlineData("SUCCEEDED")]   // order-level GET success token
    [InlineData("FINISHED")]    // notify task.state success token
    [InlineData("succeeded")]   // case-insensitive
    public async Task ApplyVendorState_TerminalSuccess_CompletesTrip(string orderState)
    {
        var trip = InProgressTrip();
        var data = new Riot3OrderQueryData { State = orderState };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.Completed);
        trip.Status.Should().Be(TripStatus.Completed);
    }

    [Fact]
    public async Task ApplyVendorState_Hang_FromInProgress_SetsHang()
    {
        var trip = InProgressTrip();
        var data = new Riot3OrderQueryData { State = "HANG" };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.Hang);
        trip.Status.Should().Be(TripStatus.Hang);
    }

    // Flavour drift: vendor moved the order HELD while DTMS holds Hang —
    // the poll re-flavours the trip so the resume command stays correct.
    [Fact]
    public async Task ApplyVendorState_Held_WhileTripHang_Reflavours()
    {
        var trip = InProgressTrip();
        trip.Pause(DTMS.Dispatch.Domain.Enums.VendorPauseSource.Hang);
        var data = new Riot3OrderQueryData { State = "HELD" };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.Held);
        trip.Status.Should().Be(TripStatus.Held);
    }

    [Fact]
    public async Task ApplyVendorState_Hang_WhileTripAlreadyHang_IsNoOp()
    {
        var trip = InProgressTrip();
        trip.Pause(DTMS.Dispatch.Domain.Enums.VendorPauseSource.Hang);
        var data = new Riot3OrderQueryData { State = "HANG" };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.None);
        trip.Status.Should().Be(TripStatus.Hang);
    }

    [Fact]
    public async Task ApplyVendorState_Processing_FromHang_Resumes()
    {
        var trip = InProgressTrip();
        trip.Pause(DTMS.Dispatch.Domain.Enums.VendorPauseSource.Hang);
        var data = new Riot3OrderQueryData { State = "PROCESSING" };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.Resumed);
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    // First-ever coverage of the REJECTED mapping — the vendor refused the
    // order post-dispatch. Distinct Rejected status since 2026-07; reason
    // precedence mirrors the webhook: ErrorDescription → ErrorCode → default.
    [Fact]
    public async Task ApplyVendorState_Rejected_RejectsTripWithVendorReason()
    {
        var trip = InProgressTrip();
        var data = new Riot3OrderQueryData
        {
            State = "REJECTED",
            FailReason = new Riot3FailResult { ErrorDescription = "station disabled" },
        };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.Rejected);
        trip.Status.Should().Be(TripStatus.Rejected);
        trip.FailureReason.Should().Be("station disabled");
    }

    [Fact]
    public async Task ApplyVendorState_Rejected_FallsBackToErrorCode()
    {
        var trip = InProgressTrip();
        var data = new Riot3OrderQueryData
        {
            State = "REJECTED",
            FailReason = new Riot3FailResult { ErrorCode = "E700404" },
        };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        trip.FailureReason.Should().Be("E700404");
    }

    // Webhook TASK_FAILED vs reconciler REJECTED race — first terminal
    // failure wins; the poll's REJECTED must not flip an already-Failed trip.
    [Fact]
    public async Task ApplyVendorState_Rejected_AfterFailed_KeepsFailed()
    {
        var trip = InProgressTrip();
        trip.MarkVendorFailed("robot stuck");
        var data = new Riot3OrderQueryData { State = "REJECTED" };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        trip.Status.Should().Be(TripStatus.Failed);
        trip.FailureReason.Should().Be("robot stuck");
    }

    // Fix A: the live PROCESSING paths must read ResolvedVehicle, not
    // ProcessingVehicle directly. Some RIOT3 deployments never populate
    // processingVehicle (nor emit a TASK_PROCESSING webhook) — they report the
    // executing robot only under executeVehicle*. Reading ProcessingVehicle
    // left the vehicle null for the whole run (this is order 4411's bug).
    [Fact]
    public async Task ApplyVendorState_Processing_CreatedTrip_CapturesExecuteVehicle_WhenNoProcessingVehicle()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");   // Created, no vehicle
        var data = new Riot3OrderQueryData
        {
            State = "PROCESSING",
            ExecuteVehicleKey = "1ee96f0f",
            ExecuteVehicleName = "FAN1_STANDARD_NO3",     // ← robot lives here, not processingVehicle
        };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.Started);
        trip.Status.Should().Be(TripStatus.InProgress);
        trip.VendorVehicleKey.Should().Be("1ee96f0f");
        trip.VendorVehicleName.Should().Be("FAN1_STANDARD_NO3");
    }

    [Fact]
    public async Task ApplyVendorState_Processing_InProgressTripMissingVehicle_BackfillsExecuteVehicle()
    {
        var trip = InProgressTrip();   // InProgress, no vehicle captured yet
        trip.VendorVehicleKey.Should().BeNull();
        var data = new Riot3OrderQueryData
        {
            State = "PROCESSING",
            ExecuteVehicleKey = "1ee96f0f",
            ExecuteVehicleName = "FAN1_STANDARD_NO3",
        };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        var transition = await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        transition.Should().Be(Riot3ReconciliationService.Transition.VehicleReassigned);
        trip.VendorVehicleKey.Should().Be("1ee96f0f");
        trip.VendorVehicleName.Should().Be("FAN1_STANDARD_NO3");
    }

    [Fact]
    public async Task ApplyVendorState_Processing_PrefersProcessingVehicleOverExecute()
    {
        // Regression guard: when RIOT3 DOES send processingVehicle, the live
        // value still wins over the terminal executeVehicle* fallback.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        var data = new Riot3OrderQueryData
        {
            State = "PROCESSING",
            ProcessingVehicle = new Riot3NotifyProcessingVehicle { Key = "live-key", Name = "LiveBot" },
            ExecuteVehicleKey = "terminal-key",
            ExecuteVehicleName = "TerminalBot",
        };
        var snapshots = Substitute.For<ITripItemSnapshotProvider>();

        await Riot3ReconciliationService.ApplyVendorStateAsync(
            trip, data, snapshots, CancellationToken.None);

        trip.VendorVehicleKey.Should().Be("live-key");
        trip.VendorVehicleName.Should().Be("LiveBot");
    }
}

// Item 2: the reconciler mission upsert must stamp each mission with the REAL
// RIOT3 time. The order-level GET reports startedTime/finishedTime and has NO
// "changeStateTime" field, so the old code fell to DateTime.UtcNow (poll time)
// and collapsed the timeline onto the poll instant — mis-ordering it against
// the sub-task webhook rows (which carry real times).
public class Riot3ReconciliationMissionUpsertTests
{
    [Fact]
    public async Task UpsertMissions_UsesRealFinishedTime_NotPollTime()
    {
        var captured = new List<TripMissionEvent>();
        var repo = Substitute.For<ITripMissionEventRepository>();
        repo.AddIfNotExistsAsync(Arg.Do<TripMissionEvent>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(true);
        var publisher = Substitute.For<ITripRealtimePublisher>();

        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                new()
                {
                    MissionKey = "m0", MissionIndex = 0, Type = "MOVE", State = "FINISHED",
                    StartedTime = "2026-07-09T08:31:41Z", FinishedTime = "2026-07-09T08:33:18Z",
                },
            },
        };

        await Riot3ReconciliationService.UpsertMissionsAsync(
            repo, publisher, Guid.NewGuid(), data, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].ChangeStateTime.Should().Be(new DateTime(2026, 7, 9, 8, 33, 18, DateTimeKind.Utc));
        captured[0].MissionIndex.Should().Be(0);   // real index from the order query is preserved
    }

    [Fact]
    public async Task UpsertMissions_ProcessingMission_UsesStartedTime()
    {
        var captured = new List<TripMissionEvent>();
        var repo = Substitute.For<ITripMissionEventRepository>();
        repo.AddIfNotExistsAsync(Arg.Do<TripMissionEvent>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(true);
        var publisher = Substitute.For<ITripRealtimePublisher>();

        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                new()
                {
                    MissionKey = "m5", MissionIndex = 5, Type = "MOVE", State = "PROCESSING",
                    StartedTime = "2026-07-09T08:33:43Z",   // no finishedTime yet
                },
            },
        };

        await Riot3ReconciliationService.UpsertMissionsAsync(
            repo, publisher, Guid.NewGuid(), data, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].ChangeStateTime.Should().Be(new DateTime(2026, 7, 9, 8, 33, 43, DateTimeKind.Utc));
    }
}
