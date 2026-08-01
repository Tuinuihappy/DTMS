using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Services;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace VendorAdapter.UnitTests;

// xmin conflict-retry re-apply semantics for the task-event webhook. When a
// save loses the commit race (operator command vs vendor echo, ~70ms apart —
// 2026-07-29 E2E, trip 5974), the webhook purges the tracker, reloads the
// trip and re-runs TryApplyTaskEventAsync on the FRESH state. These pin the
// contract that a re-apply on already-transitioned state emits NO second
// domain event: same-flavour Pause no-ops (returns true, empty event list),
// Cancel/Resume on an already-done trip throw internally and return false
// (no save at all).
public class Riot3WebhookTaskEventApplyTests
{
    private static Trip InProgressTrip()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "RIOT3-ABC");
        trip.MarkVendorStarted();
        trip.ClearDomainEvents();   // simulate events drained by the earlier save
        return trip;
    }

    private static Riot3NotifyPayload TaskPayload(string eventType) => new()
    {
        Type = "taskNotify",
        TaskEventType = eventType,
        Task = new Riot3NotifyTask { UpperKey = "upper-G1", Key = "RIOT3-ABC" },
    };

    private static Task<bool> ApplyAsync(Trip trip, string eventType) =>
        Riot3Webhooks.TryApplyTaskEventAsync(
            trip, TaskPayload(eventType), eventType, "upper-G1",
            Substitute.For<ITripItemSnapshotProvider>(),
            NullLogger<Riot3NotifyPayload>.Instance,
            CancellationToken.None);

    [Fact]
    public async Task TaskHeld_OnAlreadyHeldTrip_NoOpsWithoutSecondEvent()
    {
        var trip = InProgressTrip();
        trip.Pause(VendorPauseSource.Held);
        trip.ClearDomainEvents();

        var applied = await ApplyAsync(trip, "TASK_HELD");

        applied.Should().BeTrue();                 // falls through to a harmless no-op save
        trip.Status.Should().Be(TripStatus.Held);
        trip.DomainEvents.Should().BeEmpty();      // same-flavour guard → no 2nd TripHeld event
    }

    [Fact]
    public async Task TaskCanceled_OnAlreadyCancelledTrip_ReturnsFalse_NoSecondEvent()
    {
        var trip = InProgressTrip();
        trip.Cancel("Cancelled by operator");      // operator's command won the race
        trip.ClearDomainEvents();

        var applied = await ApplyAsync(trip, "TASK_CANCELED");

        applied.Should().BeFalse();                // IOE swallowed → caller skips the save
        trip.DomainEvents.Should().BeEmpty();      // no 2nd TripCancelled ("vendor cancelled") event
    }

    [Fact]
    public async Task TaskHeldToContinue_OnAlreadyResumedTrip_ReturnsFalse_NoSecondEvent()
    {
        var trip = InProgressTrip();               // already InProgress (resume command won)

        var applied = await ApplyAsync(trip, "TASK_HELD_TO_CONTINUE");

        applied.Should().BeFalse();                // Resume threw → swallowed
        trip.Status.Should().Be(TripStatus.InProgress);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task TaskFinished_OnInProgressTrip_AppliesNormally()
    {
        var trip = InProgressTrip();

        var applied = await ApplyAsync(trip, "TASK_FINISHED");

        applied.Should().BeTrue();
        trip.Status.Should().Be(TripStatus.Completed);
        trip.DomainEvents.Should().NotBeEmpty();   // real transition still emits its event
    }

    [Fact]
    public async Task TaskCreate_NoStateChange_ReturnsFalse()
    {
        var trip = InProgressTrip();

        var applied = await ApplyAsync(trip, "TASK_CREATE");

        applied.Should().BeFalse();                // default branch — nothing to save
        trip.Status.Should().Be(TripStatus.InProgress);
    }
}
