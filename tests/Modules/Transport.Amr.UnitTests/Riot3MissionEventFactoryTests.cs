using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace VendorAdapter.UnitTests;

// Single translation point for RIOT3 mission fields → TripMissionEvent.
// Guards the two field-semantics rules recovered from the trip 5018
// investigation (2026-07-17):
//   1. Station is trustworthy ONLY on MOVE — ACT frames carry a stale
//      last-registered dock that lags the robot by one leg.
//   2. The state-change time is picked BY STATE — a blanket
//      finishedTime ?? startedTime stamped a FAILED row with the mission's
//      start (6.5 minutes before the failure was observed).
public class Riot3MissionEventFactoryTests
{
    private static TripMissionEvent Create(
        string missionType = "MOVE",
        string state = "FINISHED",
        string? startedTime = null,
        string? finishedTime = null,
        string? stationName = null)
        => Riot3MissionEventFactory.Create(
            tripId: Guid.NewGuid(),
            missionIndex: 0,
            missionKey: "m-key",
            missionType: missionType,
            state: state,
            startedTime: startedTime,
            finishedTime: finishedTime,
            stationName: stationName,
            actionName: "ACT [4,1,0]",
            actionType: "4",
            resultCode: null,
            errorMessage: null,
            logger: NullLogger.Instance);

    // ── Rule 1: station only on MOVE ─────────────────────────────────────

    [Fact]
    public void Move_KeepsStation()
    {
        var ev = Create(missionType: "MOVE", stationName: "SHELF3");
        ev.StationName.Should().Be("SHELF3");
    }

    [Theory]
    [InlineData("ACT")]
    [InlineData("act")]       // normalization applies before the gate
    [InlineData("WAIT")]
    public void NonMove_DiscardsStation(string type)
    {
        var ev = Create(missionType: type, stationName: "SHELF3");
        ev.StationName.Should().BeNull("RIOT3's own order record binds no station to non-MOVE missions");
    }

    // ── Rule 2: time picked by state ─────────────────────────────────────

    [Fact]
    public void Finished_UsesFinishedTime()
    {
        var ev = Create(state: "FINISHED",
            startedTime: "2026-07-16T06:44:47Z", finishedTime: "2026-07-16T06:45:59Z");
        ev.ChangeStateTime.Should().Be(new DateTime(2026, 7, 16, 6, 45, 59, DateTimeKind.Utc));
    }

    [Fact]
    public void Processing_UsesStartedTime_EvenWhenLateFrameCarriesFinishedTime()
    {
        // A PROCESSING frame delivered/retried AFTER the mission finished is
        // serialized from the current record, so finishedTime rides along —
        // using it would place the row at the mission's END.
        var ev = Create(state: "PROCESSING",
            startedTime: "2026-07-16T06:44:47Z", finishedTime: "2026-07-16T06:45:59Z");
        ev.ChangeStateTime.Should().Be(new DateTime(2026, 7, 16, 6, 44, 47, DateTimeKind.Utc));
    }

    [Fact]
    public void Failed_NeverFallsBackToStartedTime()
    {
        // The real E230001 frame: startedTime only, no finishedTime. The old
        // blanket fallback stamped the failure at the mission's start.
        var ev = Create(state: "FAILED", startedTime: "2026-07-16T06:46:29Z");
        ev.ChangeStateTime.Should().NotBe(new DateTime(2026, 7, 16, 6, 46, 29, DateTimeKind.Utc));
        ev.ChangeStateTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5),
            "ingest time is the honest 'when we learned it failed' fallback");
    }

    [Fact]
    public void Canceled_PrefersFinishedTime()
    {
        var ev = Create(state: "CANCELED", finishedTime: "2026-07-16T07:00:00Z");
        ev.ChangeStateTime.Should().Be(new DateTime(2026, 7, 16, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void MalformedTime_FallsBackToNow()
    {
        var ev = Create(state: "FINISHED", finishedTime: "not-a-time");
        ev.ChangeStateTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

// The webhook and the reconciler must write IDENTICAL rows for the same
// vendor event — first-write-wins at the (TripId, MissionKey, State) unique
// index is only harmless when the two writers agree. The webhook wires the
// notify frame's fields straight into the shared factory; this test feeds the
// order-GET shape of the SAME event through UpsertMissionsAsync and compares.
public class Riot3MissionEventConsistencyTests
{
    [Fact]
    public async Task WebhookAndReconciler_ProduceIdenticalRows_ExceptMissionIndex()
    {
        var tripId = Guid.NewGuid();

        // Webhook path — the exact argument wiring HandleSubTaskEvent uses.
        var fromWebhook = Riot3MissionEventFactory.Create(
            tripId: tripId,
            missionIndex: 0,                       // sub-task payload carries no index
            missionKey: "act6a587ddde4b05147727137ce",
            missionType: "ACT",
            state: "FINISHED",
            startedTime: "2026-07-16T06:46:29Z",
            finishedTime: "2026-07-16T06:46:29Z",
            stationName: "SHELF3",                 // the stale lagging dock
            actionName: "ACT [4,14,0]",
            actionType: "4",
            resultCode: "0",
            errorMessage: null,
            logger: NullLogger.Instance);

        // Reconciler path — same event as the order-GET reports it.
        var captured = new List<TripMissionEvent>();
        var repo = Substitute.For<ITripMissionEventRepository>();
        repo.AddIfNotExistsAsync(Arg.Do<TripMissionEvent>(e => captured.Add(e)), Arg.Any<CancellationToken>())
            .Returns(new MissionUpsertResult(true, 1));

        var data = new Riot3OrderQueryData
        {
            Missions = new List<Riot3OrderMission>
            {
                new()
                {
                    MissionKey = "act6a587ddde4b05147727137ce", MissionIndex = 4,
                    Type = "ACT", State = "FINISHED",
                    StartedTime = "2026-07-16T06:46:29Z", FinishedTime = "2026-07-16T06:46:29Z",
                    StationName = null,            // order-GET gives ACT no station
                    ActionName = "ACT [4,14,0]", ActionType = "4", ResultCode = "0",
                },
            },
        };
        await Riot3ReconciliationService.UpsertMissionsAsync(
            repo, Substitute.For<ITripRealtimePublisher>(), tripId, data, CancellationToken.None);

        captured.Should().ContainSingle();
        var fromReconciler = captured[0];

        fromReconciler.MissionKey.Should().Be(fromWebhook.MissionKey);
        fromReconciler.MissionType.Should().Be(fromWebhook.MissionType);
        fromReconciler.State.Should().Be(fromWebhook.State);
        fromReconciler.StationName.Should().Be(fromWebhook.StationName,
            "the webhook must discard the stale ACT station so both sources agree on null");
        fromReconciler.ActionName.Should().Be(fromWebhook.ActionName);
        fromReconciler.ActionType.Should().Be(fromWebhook.ActionType);
        fromReconciler.ResultCode.Should().Be(fromWebhook.ResultCode);
        fromReconciler.ChangeStateTime.Should().Be(fromWebhook.ChangeStateTime);
        // MissionIndex is the documented exception: webhook rows hardcode 0.
    }
}
