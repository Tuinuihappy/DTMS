using DTMS.Dispatch.Application.Commands.AcknowledgeRobotPass;
using DTMS.Dispatch.Application.Commands.CancelTrip;
using DTMS.Dispatch.Application.Commands.PauseTrip;
using DTMS.Dispatch.Application.Commands.ResumeTrip;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Events;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UnitTests;

public class TripTests
{
    private static Trip NewEnvelopeTrip(string upperKey = "abc123-G1") =>
        Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, "ORD-1");

    [Fact]
    public void CreateForEnvelope_ProducesCreatedTripWithUpperKey()
    {
        var trip = NewEnvelopeTrip();

        trip.Status.Should().Be(TripStatus.Created);
        trip.UpperKey.Should().Be("abc123-G1");
        trip.VendorOrderKey.Should().Be("ORD-1");
        trip.JobId.Should().Be(Guid.Empty);
        trip.VehicleId.Should().BeNull();
        trip.Events.Should().ContainSingle(e => e.EventType == "EnvelopeDispatched");
    }

    [Fact]
    public void CreateForEnvelope_AllowsEmptyVendorOrderKey()
    {
        // RIOT3 occasionally returns 200 OK with no orderKey. Correlation
        // still works via UpperKey alone.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", null);

        trip.VendorOrderKey.Should().BeNull();
        trip.UpperKey.Should().Be("abc-G1");
    }

    [Fact]
    public void CreateForEnvelope_RejectsEmptyUpperKey()
    {
        var act = () => Trip.CreateForEnvelope(Guid.NewGuid(), "", "ORD");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkVendorStarted_FromCreated_TransitionsToInProgressAndBindsVehicle()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();

        trip.MarkVendorStarted(vehicle);

        trip.Status.Should().Be(TripStatus.InProgress);
        trip.VehicleId.Should().Be(vehicle);
        trip.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkVendorStarted_DuplicateWebhook_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(Guid.NewGuid());
        var startedAt = trip.StartedAt;

        var act = () => trip.MarkVendorStarted(Guid.NewGuid());

        act.Should().NotThrow();
        trip.StartedAt.Should().Be(startedAt);
    }

    [Fact]
    public void AcknowledgeBySource_FromCreated_StampsActorAndStarts()
    {
        var trip = NewEnvelopeTrip();
        var when = new DateTime(2026, 7, 6, 8, 30, 0, DateTimeKind.Utc);

        trip.AcknowledgeBySource("alice@oms", when);

        trip.Status.Should().Be(TripStatus.InProgress);
        trip.AcknowledgedBy.Should().Be("alice@oms");
        trip.AcknowledgedAt.Should().Be(when);
        trip.Events.Should().ContainSingle(e => e.EventType == "SourceAcknowledged");
    }

    [Fact]
    public void AcknowledgeBySource_SecondActor_FirstWins_ButAuditsEveryAttempt()
    {
        var trip = NewEnvelopeTrip();

        trip.AcknowledgeBySource("alice@oms");
        var firstAt = trip.AcknowledgedAt;
        trip.AcknowledgeBySource("bob@oms");   // late/duplicate ack by a different human

        // First-ack wins — the persisted actor never changes.
        trip.AcknowledgedBy.Should().Be("alice@oms");
        trip.AcknowledgedAt.Should().Be(firstAt);
        // ...but every attempt is on the audit trail.
        trip.Events.Where(e => e.EventType == "SourceAcknowledged").Should().HaveCount(2);
    }

    [Fact]
    public void AcknowledgeBySource_NullActor_Throws()
    {
        var trip = NewEnvelopeTrip();
        var act = () => trip.AcknowledgeBySource("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AcknowledgeBySource_StampsActorAndTimeOnAuditEvent()
    {
        var trip = NewEnvelopeTrip();
        var when = new DateTime(2026, 7, 6, 8, 0, 0, DateTimeKind.Utc);

        trip.AcknowledgeBySource("alice@oms", when);

        var ev = trip.Events.Single(e => e.EventType == "SourceAcknowledged");
        ev.Actor.Should().Be("alice@oms");
        ev.ActedAt.Should().Be(when);
    }

    [Fact]
    public void MarkVendorPickedUp_WithActor_StampsAuditEvent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();   // Created → InProgress
        var when = new DateTime(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc);

        trip.MarkVendorPickedUp("bob@oms", when);

        var ev = trip.Events.Single(e => e.EventType == "VendorPickupCompleted");
        ev.Actor.Should().Be("bob@oms");
        ev.ActedAt.Should().Be(when);
    }

    [Fact]
    public void MarkVendorDropCompleted_WithActor_StampsAuditEvent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();
        var when = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

        trip.MarkVendorDropCompleted(requiresDropPod: true, actor: "carol@oms", actedAt: when);

        var ev = trip.Events.Single(e => e.EventType == "VendorDropCompleted");
        ev.Actor.Should().Be("carol@oms");
        ev.ActedAt.Should().Be(when);
    }

    [Fact]
    public void MarkVendorPickedUp_CalledTwice_FiresOnce()
    {
        // Fire-once: the robot finishes several missions at the pickup station
        // (and a round-trip template revisits it), so the signal must not
        // re-fire the domain event / audit row.
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.MarkVendorPickedUp();
        trip.MarkVendorPickedUp();   // duplicate — no-op

        trip.Events.Count(e => e.EventType == "VendorPickupCompleted").Should().Be(1);
        trip.VendorPickedUpAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkVendorDropCompleted_CalledTwice_FiresOnce()
    {
        // This is order 4411's bug: MOVE-arrival + ACT at the drop station both
        // fired drop → duplicate OMS arrive-notify. Fire-once stops the second.
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.MarkVendorDropCompleted(requiresDropPod: false);
        trip.MarkVendorDropCompleted(requiresDropPod: false);   // duplicate — no-op

        trip.Events.Count(e => e.EventType == "VendorDropCompleted").Should().Be(1);
        trip.VendorDroppedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkVendorCompleted_WithActor_StampsAuditEvent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();
        var when = new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc);

        trip.MarkVendorCompleted("dave@oms", when);

        var ev = trip.Events.Single(e => e.EventType == "VendorCompleted");
        ev.Actor.Should().Be("dave@oms");
        ev.ActedAt.Should().Be(when);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void SourceActions_NonUtcActedAt_NormalizedToUtc(DateTimeKind kind)
    {
        // Source systems may send actedAt with an offset (Local) or none
        // (Unspecified); a non-UTC Kind makes Npgsql throw on the
        // `timestamp with time zone` column. All source paths must coerce to UTC.
        var trip = NewEnvelopeTrip();
        var acked = new DateTime(2026, 7, 6, 12, 0, 0, kind);
        trip.AcknowledgeBySource("alice@oms", acked);
        var dropAt = new DateTime(2026, 7, 6, 13, 0, 0, kind);
        trip.MarkVendorDropCompleted(actor: "bob@oms", actedAt: dropAt);
        var completeAt = new DateTime(2026, 7, 6, 14, 0, 0, kind);
        trip.MarkVendorCompleted("carol@oms", completeAt);

        trip.AcknowledgedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        foreach (var ev in trip.Events.Where(e => e.ActedAt is not null))
            ev.ActedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void MarkVendorPickedUp_FromAmrWebhook_NoActor_LeavesAuditActorNull()
    {
        // The AMR webhook path calls the parameterless overload — actor stays null.
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.MarkVendorPickedUp();

        var ev = trip.Events.Single(e => e.EventType == "VendorPickupCompleted");
        ev.Actor.Should().BeNull();
        ev.ActedAt.Should().BeNull();
    }

    [Fact]
    public void MarkVendorStarted_WithVendorKey_StoresVendorVehicleKey()
    {
        // RIOT3 reports processingVehicle.key as a deviceKey string. Capture
        // it verbatim so the operator dashboard can show who's executing
        // the trip — even when there's no Fleet.Vehicles entry mapped yet.
        var trip = NewEnvelopeTrip();

        trip.MarkVendorStarted(vehicleId: null, vendorVehicleKey: "Delta6FAN1");

        trip.VendorVehicleKey.Should().Be("Delta6FAN1");
        trip.VehicleId.Should().BeNull();
        trip.Status.Should().Be(TripStatus.InProgress);
        trip.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkVendorStarted_SecondCallWithDifferentKey_RecordsReassignment()
    {
        // Phase 3d (Bug #2 fix) — second TASK_PROCESSING with a different
        // vehicleKey is treated as a real reassignment, not a duplicate.
        // The cache pointer flips to the latest robot so PASS / CANCEL /
        // PAUSE commands target it, and the history table grows so ops
        // can audit "robot A → robot B at time X". Pre-3d behaviour was
        // first-write-wins which silently dropped the second key.
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1");

        trip.MarkVendorStarted(vendorVehicleKey: "Something-Else");

        trip.VendorVehicleKey.Should().Be("Something-Else");
        trip.AmrExtension!.VehicleAssignments.Should().HaveCount(2);
    }

    [Fact]
    public void MarkVendorStarted_DuplicatePayload_DoesNotGrowHistory()
    {
        // RIOT3 fires duplicate TASK_PROCESSING webhooks routinely
        // (retries, mission-state catchup). They must not pollute the
        // assignment history — idempotency lives on the extension's
        // RecordVehicleAssignment.
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1", vendorVehicleName: "FAN1_NO5");

        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1", vendorVehicleName: "FAN1_NO5");
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1", vendorVehicleName: "FAN1_NO5");

        trip.VendorVehicleKey.Should().Be("Delta6FAN1");
        trip.AmrExtension!.VehicleAssignments.Should().HaveCount(1);
    }

    // ── Retry / route context ─────────────────────────────────────────

    [Fact]
    public void CreateForEnvelope_CapturesStationContextAndAttemptOne()
    {
        var orderId = Guid.NewGuid();
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();

        var trip = Trip.CreateForEnvelope(orderId, "abc-G1", "ORD-1", pickup, drop);

        trip.PickupStationId.Should().Be(pickup);
        trip.DropStationId.Should().Be(drop);
        trip.AttemptNumber.Should().Be(1);
        trip.PreviousAttemptId.Should().BeNull();
    }

    [Fact]
    public void CreateForEnvelope_RetryLinks_PreviousAttemptId()
    {
        var original = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1",
            Guid.NewGuid(), Guid.NewGuid());
        var retry = Trip.CreateForEnvelope(
            original.DeliveryOrderId, "abc-G1-A2", "ORD-2",
            original.PickupStationId, original.DropStationId,
            attemptNumber: 2, previousAttemptId: original.Id);

        retry.AttemptNumber.Should().Be(2);
        retry.PreviousAttemptId.Should().Be(original.Id);
    }

    [Fact]
    public void CreateForEnvelope_RejectsZeroAttempt()
    {
        var act = () => Trip.CreateForEnvelope(
            Guid.NewGuid(), "abc-G1", "ORD",
            attemptNumber: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkVendorStarted_WithEmptyVendorKey_DoesNotSetField()
    {
        // Defensive: if the vendor reports processingVehicle as missing /
        // empty (e.g. assignment race), don't write whitespace to the
        // audit field.
        var trip = NewEnvelopeTrip();

        trip.MarkVendorStarted(vendorVehicleKey: "");

        trip.VendorVehicleKey.Should().BeNull();
    }

    [Fact]
    public void MarkVendorCompleted_FromCreated_CompletesAndFiresEventWithUpperKey()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorCompleted();

        trip.Status.Should().Be(TripStatus.Completed);
        trip.CompletedAt.Should().NotBeNull();
        var evt = trip.DomainEvents.OfType<TripCompletedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.DeliveryOrderId.Should().Be(trip.DeliveryOrderId);
    }

    [Fact]
    public void MarkVendorCompleted_AlreadyCompleted_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();
        var eventCount = trip.DomainEvents.OfType<TripCompletedDomainEvent>().Count();

        var act = () => trip.MarkVendorCompleted();

        act.Should().NotThrow();
        trip.DomainEvents.OfType<TripCompletedDomainEvent>().Count().Should().Be(eventCount);
    }

    [Fact]
    public void MarkVendorCompleted_FromCancelled_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.Cancel("ops");

        var act = () => trip.MarkVendorCompleted();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkVendorFailed_FromCreated_FailsAndFiresEvent()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorFailed("path blocked");

        trip.Status.Should().Be(TripStatus.Failed);
        trip.FailureReason.Should().Be("path blocked");
        var evt = trip.DomainEvents.OfType<TripFailedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.Reason.Should().Be("path blocked");
    }

    [Fact]
    public void MarkVendorFailed_AlreadyFailed_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorFailed("first");
        var act = () => trip.MarkVendorFailed("second");
        act.Should().NotThrow();
        trip.FailureReason.Should().Be("first");
    }

    [Fact]
    public void MarkVendorFailed_FromCompleted_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();

        var act = () => trip.MarkVendorFailed("late");

        act.Should().Throw<InvalidOperationException>();
    }

    // First-terminal-failure-wins: a late TASK_FAILED after an operator
    // cancel must NOT overwrite Cancelled (the cancel already released the
    // order's items — flipping to Failed would drag the order down).
    [Fact]
    public void MarkVendorFailed_FromCancelled_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.Cancel("ops cancel");

        var act = () => trip.MarkVendorFailed("late vendor failure");

        act.Should().NotThrow();
        trip.Status.Should().Be(TripStatus.Cancelled);
        trip.DomainEvents.OfType<TripFailedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void MarkVendorRejected_FromCreated_RejectsAndFiresEvent()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorRejected("station disabled");

        trip.Status.Should().Be(TripStatus.Rejected);
        trip.FailureReason.Should().Be("station disabled");
        trip.CompletedAt.Should().NotBeNull();
        var evt = trip.DomainEvents.OfType<TripRejectedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.Reason.Should().Be("station disabled");
    }

    [Fact]
    public void MarkVendorRejected_AlreadyRejected_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorRejected("first");

        var act = () => trip.MarkVendorRejected("second");

        act.Should().NotThrow();
        trip.FailureReason.Should().Be("first");
        trip.DomainEvents.OfType<TripRejectedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void MarkVendorRejected_FromCompleted_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();

        var act = () => trip.MarkVendorRejected("late");

        act.Should().Throw<InvalidOperationException>();
    }

    // Webhook TASK_FAILED and reconciler REJECTED can race for the same
    // trip — whichever lands first wins; the second must not flip the
    // status or emit a second order-failing event.
    [Fact]
    public void MarkVendorRejected_FromFailed_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorFailed("robot stuck");

        trip.MarkVendorRejected("late reject");

        trip.Status.Should().Be(TripStatus.Failed);
        trip.DomainEvents.OfType<TripRejectedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void MarkVendorFailed_FromRejected_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorRejected("station disabled");

        trip.MarkVendorFailed("late failure");

        trip.Status.Should().Be(TripStatus.Rejected);
        trip.DomainEvents.OfType<TripFailedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void MarkVendorRejected_FromCancelled_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.Cancel("ops cancel");

        trip.MarkVendorRejected("late reject");

        trip.Status.Should().Be(TripStatus.Cancelled);
        trip.DomainEvents.OfType<TripRejectedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void MarkVendorCompleted_FromRejected_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorRejected("station disabled");

        var act = () => trip.MarkVendorCompleted();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_FromRejected_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorRejected("station disabled");

        var act = () => trip.Cancel("ops");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetAssignedVehicle_FirstAssignment_Binds()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();

        trip.SetAssignedVehicle(vehicle);

        trip.VehicleId.Should().Be(vehicle);
    }

    [Fact]
    public void SetAssignedVehicle_SameVehicle_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();
        trip.SetAssignedVehicle(vehicle);

        var act = () => trip.SetAssignedVehicle(vehicle);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetAssignedVehicle_DifferentVehicle_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.SetAssignedVehicle(Guid.NewGuid());

        var act = () => trip.SetAssignedVehicle(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PauseAndResume_Held_RoundTrip()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.Pause(VendorPauseSource.Held);
        trip.Status.Should().Be(TripStatus.Held);
        trip.DomainEvents.OfType<TripHeldDomainEvent>().Single().Reflavour.Should().BeFalse();

        trip.Resume();
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public void PauseAndResume_Hang_RoundTrip()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.Pause(VendorPauseSource.Hang);
        trip.Status.Should().Be(TripStatus.Hang);
        trip.DomainEvents.OfType<TripHangDomainEvent>().Single().Reflavour.Should().BeFalse();

        trip.Resume();
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    // Vendor flavour drift — RIOT3 can move an order HELD↔HANG mid-pause.
    // Status follows the vendor; the event is flagged Reflavour so the
    // facts projector doesn't count it as a fresh pause.
    [Fact]
    public void Pause_Reflavour_HangToHeld_FlipsStatusAndFlagsEvent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();
        trip.Pause(VendorPauseSource.Hang);

        trip.Pause(VendorPauseSource.Held);

        trip.Status.Should().Be(TripStatus.Held);
        trip.DomainEvents.OfType<TripHeldDomainEvent>().Single().Reflavour.Should().BeTrue();
    }

    [Fact]
    public void Pause_SameFlavourAgain_IsIdempotentNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();
        trip.Pause(VendorPauseSource.Hang);

        trip.Pause(VendorPauseSource.Hang);

        trip.Status.Should().Be(TripStatus.Hang);
        trip.DomainEvents.OfType<TripHangDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Pause_FromCreated_Throws()
    {
        var trip = NewEnvelopeTrip();

        var act = () => trip.Pause(VendorPauseSource.Hang);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AcknowledgeRobotPass_FromInProgressWithVehicleKey_RaisesEventAndKeepsStatus()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1");

        trip.AcknowledgeRobotPass();

        // PASS is a nudge — status must NOT change.
        trip.Status.Should().Be(TripStatus.InProgress);
        var evt = trip.DomainEvents.OfType<TripRobotPassAcknowledgedDomainEvent>().Single();
        evt.VendorVehicleKey.Should().Be("Delta6FAN1");
        evt.TripId.Should().Be(trip.Id);
        trip.Events.Should().Contain(e => e.EventType == "RobotPassAcknowledged" && e.Details == "Delta6FAN1");
    }

    [Fact]
    public void AcknowledgeRobotPass_FromCreated_Throws()
    {
        var trip = NewEnvelopeTrip(); // Created — no vendor key, status not InProgress

        var act = () => trip.AcknowledgeRobotPass();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*InProgress*");
    }

    [Fact]
    public void AcknowledgeRobotPass_FromPaused_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(vendorVehicleKey: "Delta6FAN1");
        trip.Pause(VendorPauseSource.Held);

        var act = () => trip.AcknowledgeRobotPass();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*InProgress*");
    }

    [Fact]
    public void AcknowledgeRobotPass_WithoutVendorVehicleKey_Throws()
    {
        var trip = NewEnvelopeTrip();
        // MarkVendorStarted with no vendor key — status flips to InProgress
        // but VendorVehicleKey stays null. RIOT3 routes PASS by deviceKey
        // so we can't proceed.
        trip.MarkVendorStarted(vehicleId: Guid.NewGuid(), vendorVehicleKey: null);

        var act = () => trip.AcknowledgeRobotPass();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*vehicle key*");
    }

    [Fact]
    public void Cancel_FromCreated_Cancels()
    {
        var trip = NewEnvelopeTrip();

        trip.Cancel("operator");

        trip.Status.Should().Be(TripStatus.Cancelled);
    }
}

public class EnvelopeUpperKeyTests
{
    [Fact]
    public void Build_FirstAttempt_OmitsAttemptSuffix()
    {
        // Backward compat — RIOT3 + persisted rows must round-trip unchanged
        // for first attempts.
        var orderId = Guid.NewGuid();
        var key = DTMS.SharedKernel.EnvelopeUpperKey.Build(orderId, 1);
        key.Should().NotContain("-A");
        key.Should().EndWith("-G1");
    }

    [Fact]
    public void Build_RetryAttempt_AppendsAttemptSuffix()
    {
        var orderId = Guid.NewGuid();
        var key = DTMS.SharedKernel.EnvelopeUpperKey.Build(orderId, 1, attemptNumber: 3);
        key.Should().EndWith("-G1-A3");
    }

    [Fact]
    public void TryParse_LegacyShape_ReturnsAttemptOne()
    {
        var key = "48752c3e35bb4d0db227cbde6c1da95b-G2";
        var ok = DTMS.SharedKernel.EnvelopeUpperKey.TryParse(
            key, out _, out var group, out var attempt);
        ok.Should().BeTrue();
        group.Should().Be(2);
        attempt.Should().Be(1);
    }

    [Fact]
    public void TryParse_RetryShape_ReturnsAttemptNumber()
    {
        var key = "48752c3e35bb4d0db227cbde6c1da95b-G2-A5";
        var ok = DTMS.SharedKernel.EnvelopeUpperKey.TryParse(
            key, out _, out var group, out var attempt);
        ok.Should().BeTrue();
        group.Should().Be(2);
        attempt.Should().Be(5);
    }

    [Fact]
    public void TryParse_2OutOverload_StillWorks()
    {
        // Existing webhook + reconciler callers use the 2-out overload.
        var ok = DTMS.SharedKernel.EnvelopeUpperKey.TryParse(
            "48752c3e35bb4d0db227cbde6c1da95b-G1-A2", out _, out var group);
        ok.Should().BeTrue();
        group.Should().Be(1);
    }
}

// ── Handler tests: vendor-first persistence semantics ───────────────────
//
// Cancel/Pause/Resume handlers call the vendor BEFORE persisting the local
// state transition. If the vendor rejects, UpdateAsync must NOT be called
// (the in-memory mutation is discarded by scope disposal). If the domain
// rejects the transition, the vendor must NOT be called.

public class CancelTripCommandHandlerTests
{
    [Fact]
    public async Task VendorSuccess_PersistsCancelledState()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        // Handler routes envelope ops through vendorOrderKey not upperKey
        // (per commit 107675e — RIOT3 silently no-ops upperKey route).
        vendor.CancelCalls.Should().ContainSingle().Which.Should().Be(trip.VendorOrderKey);
    }

    [Fact]
    public async Task VendorFailure_DoesNotPersist()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextError = "network blip" };

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("network blip");
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task DomainReject_DoesNotCallVendor()
    {
        var trip = NewTripInProgress();
        trip.Cancel("first"); // already cancelled
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "second"), default);

        result.IsFailure.Should().BeTrue();
        vendor.CancelCalls.Should().BeEmpty();
        repo.UpdateCalls.Should().Be(0);
    }

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}

public class PauseAndResumeTripHandlerTests
{
    [Fact]
    public async Task Pause_VendorSuccess_PersistsPausedState()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        // Handler routes envelope ops through vendorOrderKey not upperKey
        // (per commit 107675e — RIOT3 silently no-ops upperKey route).
        vendor.PauseCalls.Should().ContainSingle().Which.Should().Be(trip.VendorOrderKey);
    }

    [Fact]
    public async Task Pause_VendorFailure_DoesNotPersist()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextError = "vendor 500" };

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_FromPaused_VendorSuccess_Resumes()
    {
        var trip = NewTripInProgress();
        trip.Pause(VendorPauseSource.Held);
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        // Handler routes envelope ops through vendorOrderKey not upperKey
        // (per commit 107675e — RIOT3 silently no-ops upperKey route).
        vendor.ResumeCalls.Should().ContainSingle().Which.Should().Be(trip.VendorOrderKey);
        // E639999 net: a Held trip must NEVER produce CONTINUE_FROM_HANG.
        vendor.ResumeFromHangCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_FromHang_SendsContinueFromHangOnly()
    {
        var trip = NewTripInProgress();
        trip.Pause(VendorPauseSource.Hang);
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        vendor.ResumeFromHangCalls.Should().ContainSingle().Which.Should().Be(trip.VendorOrderKey);
        // E639999 net: a Hang trip must NEVER produce CONTINUE_FROM_HELD.
        vendor.ResumeCalls.Should().BeEmpty();
    }

    // Drift + resume: vendor re-flavoured Hang→Held mid-pause; the resume
    // command must follow the CURRENT status, not the original flavour.
    [Fact]
    public async Task Resume_AfterReflavourHangToHeld_SendsContinueFromHeld()
    {
        var trip = NewTripInProgress();
        trip.Pause(VendorPauseSource.Hang);
        trip.Pause(VendorPauseSource.Held);   // drift
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        vendor.ResumeCalls.Should().ContainSingle();
        vendor.ResumeFromHangCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_DomainReject_DoesNotCallVendor()
    {
        var trip = NewTripInProgress(); // InProgress, not Paused
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        vendor.ResumeCalls.Should().BeEmpty();
    }

    // ── Gap #6: Vendor NoVendorRecord policy per command ────────────────

    [Fact]
    public async Task Cancel_VendorNoRecord_GracefulSuccess_PersistsCancellation()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextOutcome = VendorOperationOutcome.NoVendorRecord };

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsSuccess.Should().BeTrue();           // operator intent met
        repo.UpdateCalls.Should().Be(1);              // trip saved as Cancelled
        trip.Status.Should().Be(TripStatus.Cancelled);
    }

    [Fact]
    public async Task Pause_VendorNoRecord_AutoMarksFailed_ReturnsActionableError()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextOutcome = VendorOperationOutcome.NoVendorRecord };

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();           // operator told to take action
        result.Error!.ToLowerInvariant().Should().Contain("vendor has no record");
        result.Error.Should().Contain("/reopen");   // next-step guidance
        trip.Status.Should().Be(TripStatus.Failed);    // auto-reconciled
        repo.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Resume_VendorNoRecord_AutoMarksFailed_ReturnsActionableError()
    {
        var trip = NewTripInProgress();
        trip.Pause(VendorPauseSource.Held);
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextOutcome = VendorOperationOutcome.NoVendorRecord };

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error!.ToLowerInvariant().Should().Contain("vendor has no record");
        trip.Status.Should().Be(TripStatus.Failed);
        repo.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_VendorRejected_DoesNotPersistAndReturnsFailure()
    {
        // Distinguish "rejected with reason" from "no record" — Rejected
        // must NOT cancel locally (otherwise we hide vendor business rules
        // like permissions / locked orders from the operator).
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextError = "RIOT3 rejected cancel (code E100007): not allowed" };

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("E100007");
        repo.UpdateCalls.Should().Be(0);             // trip stays InProgress
    }

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}

// ── ReissueTripCommandHandler — parent-order guard (Bug #1 fix) ─────────

public class ReissueTripGuardTests
{
    [Fact]
    public async Task Retry_BlockedWhenParentOrderCancelled()
    {
        var cancelled = NewCancelledTrip();
        var handler = NewHandler(cancelled, orderStatus: "Cancelled");

        var result = await handler.Handle(
            new DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommand(
                cancelled.Id, "Manual", null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Cancelled");
        // Cancelled orders are recoverable via /reopen (same as Failed) —
        // the hint must point the operator at the sanctioned 2-step path.
        result.Error.Should().Contain("/reopen");
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Completed")]
    [InlineData("PartiallyCompleted")]
    public async Task Retry_BlockedForAllTerminalOrderStatuses(string status)
    {
        var cancelled = NewCancelledTrip();
        var handler = NewHandler(cancelled, orderStatus: status);

        var result = await handler.Handle(
            new DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommand(
                cancelled.Id, "Manual", null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain(status);
    }

    [Fact]
    public async Task Retry_AllowedWhenOrderConfirmedAndTripCancelled()
    {
        var cancelled = NewCancelledTrip();
        var handler = NewHandler(cancelled, orderStatus: "Confirmed");

        var result = await handler.Handle(
            new DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommand(
                cancelled.Id, "Manual", "ops", "valid retry"),
            default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Retry_BlockedWhenOrderNotFound()
    {
        var cancelled = NewCancelledTrip();
        var handler = NewHandler(cancelled, orderStatus: null);

        var result = await handler.Handle(
            new DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommand(
                cancelled.Id, "Manual", null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Retry_AllowedForFailedTripWhenOrderReopenedToConfirmed()
    {
        // BUG #3 path: Trip TASK_FAILED → Order Failed → operator /reopen
        // → Order Confirmed → operator /retry on the Failed Trip succeeds.
        var failed = NewFailedTrip();
        var handler = NewHandler(failed, orderStatus: "Confirmed");

        var result = await handler.Handle(
            new DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommand(
                failed.Id, "Manual", "ops", "retry after reopen"),
            default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Retry_BlockedForFailedTripWhenOrderStillFailed()
    {
        // The 2-step audit trail: operator MUST reopen the order first.
        var failed = NewFailedTrip();
        var handler = NewHandler(failed, orderStatus: "Failed");

        var result = await handler.Handle(
            new DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommand(
                failed.Id, "Manual", null, null),
            default);

        // Order-status guard fires before Trip status check; both messages
        // are operator-friendly. Either contains "Failed" + "Reopen" hint.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Failed");
    }

    private static Trip NewCancelledTrip()
    {
        var t = Trip.CreateForEnvelope(
            Guid.NewGuid(), "abc-G1", "ORD-1",
            pickupStationId: Guid.NewGuid(),
            dropStationId: Guid.NewGuid());
        t.Cancel("operator");
        return t;
    }

    private static Trip NewFailedTrip()
    {
        var t = Trip.CreateForEnvelope(
            Guid.NewGuid(), "abc-G1", "ORD-1",
            pickupStationId: Guid.NewGuid(),
            dropStationId: Guid.NewGuid());
        t.MarkVendorStarted(Guid.NewGuid());
        t.MarkVendorFailed("simulated vendor failure");
        return t;
    }

    private static DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommandHandler
        NewHandler(Trip trip, string? orderStatus)
    {
        return new(
            new FakeTripRepository(trip),
            new StubRetryEventRepository(),
            new StubRetryDispatcher(),
            new StubOrderStatusReader(orderStatus),
            NullLogger<DTMS.Dispatch.Application.Commands.ReissueTrip.ReissueTripCommandHandler>.Instance);
    }
}

internal sealed class StubOrderStatusReader : IDeliveryOrderStatusReader
{
    private readonly string? _status;
    public StubOrderStatusReader(string? status) => _status = status;
    public Task<string?> GetStatusAsync(Guid orderId, CancellationToken ct = default)
        => Task.FromResult(_status);
    public Task<bool?> GetRequiresDropPodAsync(Guid orderId, CancellationToken ct = default)
        => Task.FromResult<bool?>(null);
    public Task<string?> GetSourceSystemKeyAsync(Guid orderId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal sealed class StubRetryEventRepository : ITripRetryEventRepository
{
    public Task AddAsync(TripRetryEvent ev, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<TripRetryEvent>> GetByOriginalTripIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(new List<TripRetryEvent>());
    public Task<List<TripRetryEvent>> GetByDeliveryOrderIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(new List<TripRetryEvent>());
}

internal sealed class StubRetryDispatcher : ITripRetryDispatcher
{
    public Task<Result<Guid>> ReissueAsync(
        Guid deliveryOrderId, Guid pickupStationId, Guid dropStationId,
        string newUpperKey, int attemptNumber, Guid previousAttemptId,
        Guid? jobId,
        CancellationToken ct = default)
        => Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
}

// ── xmin conflict-retry: command vs vendor-echo webhook race ────────────
//
// RIOT3 accepts a pause/resume/cancel and fires its echo webhook
// (TASK_HELD / *_TO_CONTINUE / TASK_CANCELED) within ~70ms — often before
// the command's own save commits (2026-07-29 E2E, trip 5974). With the
// xmin token the command's save throws DbUpdateConcurrencyException; the
// handler must purge the tracker, reload fresh, re-apply (domain guards
// turn an already-applied transition into a no-op so NO second domain
// event is emitted) and still report Success. The vendor call must never
// be repeated; a second consecutive conflict propagates (middleware 409).

public class TripCommandConflictRetryTests
{
    [Fact]
    public async Task Pause_EchoWonRace_ResolvesAsNoOpSuccess_WithoutSecondEvent()
    {
        var trip = NewTripInProgress();
        var reloaded = NewTripInProgress();
        reloaded.Pause(VendorPauseSource.Held);   // echo already committed Held
        reloaded.ClearDomainEvents();             // and its events were drained by that save
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 1, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        vendor.PauseCalls.Should().HaveCount(1);   // vendor call never repeated
        repo.ResetTrackingCalls.Should().Be(1);    // poisoned tracker purged before reload
        repo.UpdateCalls.Should().Be(2);           // failed save + retry save
        reloaded.Status.Should().Be(TripStatus.Held);
        reloaded.DomainEvents.Should().BeEmpty();  // same-flavour guard no-opped → no 2nd TripHeld event
    }

    [Fact]
    public async Task Pause_ConflictSupersededByTerminal_ReturnsFailureWithActualStatus()
    {
        var trip = NewTripInProgress();
        var reloaded = NewTripInProgress();
        reloaded.MarkVendorCompleted();            // TASK_FINISHED won the race instead
        reloaded.ClearDomainEvents();
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 1, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("superseded").And.Contain("Completed");
        repo.UpdateCalls.Should().Be(1);           // retry did not save anything
        reloaded.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Pause_SecondConsecutiveConflict_Propagates()
    {
        var trip = NewTripInProgress();
        var reloaded = NewTripInProgress();        // still InProgress → retry re-applies Pause
        reloaded.ClearDomainEvents();
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 2, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var act = () => handler.Handle(new PauseTripCommand(trip.Id), default);

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>();
        vendor.PauseCalls.Should().HaveCount(1);   // still never repeated
    }

    [Fact]
    public async Task Resume_EchoWonRace_ResolvesAsNoOpSuccess_WithoutSecondEvent()
    {
        var trip = NewTripInProgress();
        trip.Pause(VendorPauseSource.Held);
        var reloaded = NewTripInProgress();        // echo already resumed → InProgress
        reloaded.ClearDomainEvents();
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 1, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        vendor.ResumeCalls.Should().HaveCount(1);
        vendor.ResumeFromHangCalls.Should().BeEmpty();
        repo.ResetTrackingCalls.Should().Be(1);
        reloaded.Status.Should().Be(TripStatus.InProgress);
        reloaded.DomainEvents.Should().BeEmpty();  // Resume threw IOE, swallowed → no 2nd TripResumed event
    }

    [Fact]
    public async Task Resume_ConflictSupersededByTerminal_ReturnsFailureWithActualStatus()
    {
        var trip = NewTripInProgress();
        trip.Pause(VendorPauseSource.Hang);
        var reloaded = NewTripInProgress();
        reloaded.MarkVendorFailed("robot fault");  // TASK_FAILED won the race
        reloaded.ClearDomainEvents();
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 1, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("superseded").And.Contain("Failed");
        reloaded.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_EchoWonRace_PreservesOperatorReasonAsAuditEvent()
    {
        var trip = NewTripInProgress();
        var reloaded = NewTripInProgress();
        reloaded.Cancel("vendor cancelled");       // echo's placeholder reason won first-write-wins
        reloaded.ClearDomainEvents();
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 1, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "operator stop — wrong station"), default);

        result.IsSuccess.Should().BeTrue();        // operator intent satisfied either way
        vendor.CancelCalls.Should().HaveCount(1);
        reloaded.Status.Should().Be(TripStatus.Cancelled);
        reloaded.DomainEvents.Should().BeEmpty();  // no 2nd TripCancelled integration event
        // ...but the operator's true reason is preserved as an audit event
        reloaded.Events.Should().Contain(e =>
            e.EventType == "OperatorCancelSuperseded" &&
            e.Details!.Contains("operator stop — wrong station"));
    }

    [Fact]
    public async Task Cancel_ConflictWithNonTerminalWinner_AppliesFreshCancel()
    {
        var trip = NewTripInProgress();
        var reloaded = NewTripInProgress();
        reloaded.Pause(VendorPauseSource.Held);    // a TASK_HELD write won, not a cancel
        reloaded.ClearDomainEvents();
        var repo = new FakeTripRepository(trip) { ConcurrencyThrows = 1, ReloadedTrip = reloaded };
        var vendor = new StubVendorOps();

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsSuccess.Should().BeTrue();
        reloaded.Status.Should().Be(TripStatus.Cancelled);
        reloaded.DomainEvents.Should().ContainSingle();   // real cancel applied on the fresh state
        reloaded.Events.Should().Contain(e => e.EventType == "TripCancelled" && e.Details == "ops");
    }

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}

// ── Test doubles ────────────────────────────────────────────────────────

internal sealed class FakeTripRepository : ITripRepository
{
    private readonly Trip _trip;
    public int UpdateCalls { get; private set; }
    public int ResetTrackingCalls { get; private set; }

    // xmin conflict-retry hooks: the next N UpdateAsync calls throw
    // DbUpdateConcurrencyException (simulating a vendor-echo webhook winning
    // the commit race); after ResetTracking, Get* return ReloadedTrip (the
    // "fresh from DB" instance) when set.
    public int ConcurrencyThrows { get; set; }
    public Trip? ReloadedTrip { get; set; }

    private Trip Current => ResetTrackingCalls > 0 && ReloadedTrip is not null ? ReloadedTrip : _trip;

    public FakeTripRepository(Trip trip) { _trip = trip; }

    public Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<Trip?>(id == _trip.Id ? Current : null);

    public Task<Trip?> GetByUpperKeyAsync(string upperKey, CancellationToken ct = default)
        => Task.FromResult<Trip?>(upperKey == _trip.UpperKey ? Current : null);

    public Task<Trip?> GetByVendorOrderKeyAsync(string vendorOrderKey, CancellationToken ct = default)
        => Task.FromResult<Trip?>(vendorOrderKey == _trip.VendorOrderKey ? Current : null);

    public Task<TripSubTaskLookup?> GetSubTaskLookupAsync(string? upperKey, string? vendorOrderKey, CancellationToken ct = default)
        => Task.FromResult<TripSubTaskLookup?>(
            upperKey == _trip.UpperKey || vendorOrderKey == _trip.VendorOrderKey
                ? new TripSubTaskLookup(_trip.Id, _trip.VendorPickedUpAt, _trip.VendorDroppedAt)
                : null);

    public Task<Guid> GetRootTripIdAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(tripId);

    public Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken ct = default)
        => Task.FromResult(new List<Trip>());

    public Task<List<Trip>> GetInFlightEnvelopeTripsAsync(CancellationToken ct = default)
        => Task.FromResult(new List<Trip>());

    public Task<List<Trip>> GetTerminalTripsMissingVehicleAsync(DateTime completedSinceUtc, CancellationToken ct = default)
        => Task.FromResult(new List<Trip>());

    public Task<List<Trip>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken ct = default)
        => Task.FromResult(deliveryOrderId == _trip.DeliveryOrderId ? new List<Trip> { _trip } : new List<Trip>());

    public Task AddAsync(Trip trip, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(Trip trip, CancellationToken ct = default)
    {
        UpdateCalls++;
        if (ConcurrencyThrows > 0)
        {
            ConcurrencyThrows--;
            throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "simulated xmin conflict — concurrent writer committed first");
        }
        return Task.CompletedTask;
    }

    public void ResetTracking() => ResetTrackingCalls++;

    // WMS PR-4b (PR-F) — Fake CAS: this test double doesn't need pool
    // semantics for the existing suite. Real behavior is covered in
    // Dispatch.IntegrationTests (raw SQL against a real Postgres).
    public Task<bool> TryClaimFromPoolAsync(Guid tripId, Guid operatorId, CancellationToken ct = default)
        => Task.FromResult(false);
}

// ── AcknowledgeRobotPass handler tests ──────────────────────────────────
//
// PASS targets the deviceKey (Trip.VendorVehicleKey), not the orderKey.
// On NoVendorRecord, unlike Pause/Resume, the handler does NOT auto-fail
// the Trip — it's still in-flight; only the operator can decide.

public class AcknowledgeRobotPassHandlerTests
{
    [Fact]
    public async Task VendorSuccess_PersistsAndKeepsStatusInProgress()
    {
        var trip = NewTripInProgressWithKey();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubRobotOps();

        var handler = new AcknowledgeRobotPassCommandHandler(
            repo, vendor, NullLogger<AcknowledgeRobotPassCommandHandler>.Instance);
        var result = await handler.Handle(new AcknowledgeRobotPassCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        vendor.PassCalls.Should().ContainSingle().Which.Should().Be("Delta6FAN1");
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public async Task TripNotFound_ReturnsFailure()
    {
        var trip = NewTripInProgressWithKey();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubRobotOps();

        var handler = new AcknowledgeRobotPassCommandHandler(
            repo, vendor, NullLogger<AcknowledgeRobotPassCommandHandler>.Instance);
        var result = await handler.Handle(new AcknowledgeRobotPassCommand(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
        vendor.PassCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DomainReject_WrongStatus_DoesNotCallVendor()
    {
        var trip = NewTripInProgressWithKey();
        trip.Pause(VendorPauseSource.Held);
        var repo = new FakeTripRepository(trip);
        var vendor = new StubRobotOps();

        var handler = new AcknowledgeRobotPassCommandHandler(
            repo, vendor, NullLogger<AcknowledgeRobotPassCommandHandler>.Instance);
        var result = await handler.Handle(new AcknowledgeRobotPassCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        vendor.PassCalls.Should().BeEmpty();
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task DomainReject_NoVendorVehicleKey_DoesNotCallVendor()
    {
        // InProgress trip but no deviceKey captured (e.g. vendor never reported it).
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        trip.MarkVendorStarted(vehicleId: Guid.NewGuid(), vendorVehicleKey: null);
        var repo = new FakeTripRepository(trip);
        var vendor = new StubRobotOps();

        var handler = new AcknowledgeRobotPassCommandHandler(
            repo, vendor, NullLogger<AcknowledgeRobotPassCommandHandler>.Instance);
        var result = await handler.Handle(new AcknowledgeRobotPassCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        vendor.PassCalls.Should().BeEmpty();
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task VendorNoRecord_DoesNotPersist_AndDoesNotAutoFailTrip()
    {
        // Critical contrast with Pause/Resume: PASS NoVendorRecord must
        // leave the Trip InProgress so the operator can investigate at
        // the floor (the robot may have already moved on).
        var trip = NewTripInProgressWithKey();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubRobotOps { NextOutcome = VendorOperationOutcome.NoVendorRecord };

        var handler = new AcknowledgeRobotPassCommandHandler(
            repo, vendor, NullLogger<AcknowledgeRobotPassCommandHandler>.Instance);
        var result = await handler.Handle(new AcknowledgeRobotPassCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error!.ToLowerInvariant().Should().Contain("no record");
        trip.Status.Should().Be(TripStatus.InProgress); // NOT Failed
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task VendorRejected_DoesNotPersist_AndSurfacesVendorMessage()
    {
        var trip = NewTripInProgressWithKey();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubRobotOps { NextError = "RIOT3 rejected pass (code E100099): robot offline" };

        var handler = new AcknowledgeRobotPassCommandHandler(
            repo, vendor, NullLogger<AcknowledgeRobotPassCommandHandler>.Instance);
        var result = await handler.Handle(new AcknowledgeRobotPassCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("E100099");
        trip.Status.Should().Be(TripStatus.InProgress);
        repo.UpdateCalls.Should().Be(0);
    }

    private static Trip NewTripInProgressWithKey()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(vehicleId: Guid.NewGuid(), vendorVehicleKey: "Delta6FAN1");
        return t;
    }
}

internal sealed class StubRobotOps : IVendorRobotOperationService
{
    public VendorOperationOutcome NextOutcome { get; set; } = VendorOperationOutcome.Accepted;
    public string? NextError { get; set; }
    public List<string> PassCalls { get; } = new();

    public Task<Result<VendorOperationOutcome>> PassAsync(string vendorVehicleKey, CancellationToken ct = default)
    {
        PassCalls.Add(vendorVehicleKey);
        return Task.FromResult(NextError is not null
            ? Result<VendorOperationOutcome>.Failure(NextError)
            : Result<VendorOperationOutcome>.Success(NextOutcome));
    }
}

internal sealed class StubVendorOps : IVendorEnvelopeOperationService
{
    // Default to vendor-accepted so existing tests that don't care keep
    // exercising the happy path. Override per-test via NextOutcome /
    // NextError to drive the new outcome branches.
    public VendorOperationOutcome NextOutcome { get; set; } = VendorOperationOutcome.Accepted;
    public string? NextError { get; set; }
    public List<string> CancelCalls { get; } = new();
    public List<string> PauseCalls { get; } = new();
    public List<string> ResumeCalls { get; } = new();
    public List<string> ResumeFromHangCalls { get; } = new();

    public Task<Result<VendorOperationOutcome>> CancelAsync(string upperKey, CancellationToken ct = default)
    {
        CancelCalls.Add(upperKey);
        return Task.FromResult(BuildResult());
    }

    public Task<Result<VendorOperationOutcome>> PauseAsync(string upperKey, CancellationToken ct = default)
    {
        PauseCalls.Add(upperKey);
        return Task.FromResult(BuildResult());
    }

    public Task<Result<VendorOperationOutcome>> ResumeAsync(string upperKey, CancellationToken ct = default)
    {
        ResumeCalls.Add(upperKey);
        return Task.FromResult(BuildResult());
    }

    public Task<Result<VendorOperationOutcome>> ResumeFromHangAsync(string upperKey, CancellationToken ct = default)
    {
        ResumeFromHangCalls.Add(upperKey);
        return Task.FromResult(BuildResult());
    }

    private Result<VendorOperationOutcome> BuildResult()
        => NextError is not null
            ? Result<VendorOperationOutcome>.Failure(NextError)
            : Result<VendorOperationOutcome>.Success(NextOutcome);
}
