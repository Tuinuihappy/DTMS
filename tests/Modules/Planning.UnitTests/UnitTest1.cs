using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using FluentAssertions;

namespace Planning.UnitTests;

public class JobTests
{
    [Fact]
    public void NewJob_ShouldHaveCreatedStatus()
    {
        var job = new Job(Guid.NewGuid(), "High");

        job.Status.Should().Be(JobStatus.Created);
        job.Priority.Should().Be("High");
        job.AssignedVehicleId.Should().BeNull();
        job.Legs.Should().BeEmpty();
    }

    [Fact]
    public void AddLeg_ShouldAddToCollection()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var leg = job.AddLeg(from, to, 1, 15.5);

        job.Legs.Should().HaveCount(1);
        job.EstimatedDistance.Should().Be(15.5);
        leg.FromStationId.Should().Be(from);
        leg.ToStationId.Should().Be(to);
    }

    [Fact]
    public void AssignVehicle_ShouldChangeStatusToAssigned()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        var vehicleId = Guid.NewGuid();

        job.AssignVehicle(vehicleId, 120);

        job.Status.Should().Be(JobStatus.Assigned);
        job.AssignedVehicleId.Should().Be(vehicleId);
        job.EstimatedDuration.Should().Be(120);
    }

    [Fact]
    public void AssignVehicle_WhenNotCreated_ShouldThrow()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.AssignVehicle(Guid.NewGuid(), 60);

        var act = () => job.AssignVehicle(Guid.NewGuid(), 60);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Commit_ShouldChangeStatusToCommitted()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.AssignVehicle(Guid.NewGuid(), 60);

        job.Commit();

        job.Status.Should().Be(JobStatus.Committed);
    }

    [Fact]
    public void Commit_WhenNotAssigned_ShouldSucceed()
    {
        // Phase 1 flow: planners can commit without pre-assigning a vehicle.
        // RIOT3 auto-selects the robot when the order has no
        // appointVehicleKey, and the assigned vehicle is reported back via
        // the task callback (processingVehicle.key).
        var job = new Job(Guid.NewGuid(), "Normal");

        var act = () => job.Commit();

        act.Should().NotThrow();
        job.Status.Should().Be(JobStatus.Committed);
        job.AssignedVehicleId.Should().BeNull();
    }

    [Fact]
    public void Commit_WhenAlreadyCommitted_ShouldThrow()
    {
        // Guard the other side of the transition: Committed is a terminal
        // status from Planning's perspective; re-committing is an error.
        var job = new Job(Guid.NewGuid(), "Normal");
        job.Commit();

        var act = () => job.Commit();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddMultipleLegs_ShouldAccumulateDistance()
    {
        var job = new Job(Guid.NewGuid(), "Normal");

        job.AddLeg(Guid.NewGuid(), Guid.NewGuid(), 1, 10.0);
        job.AddLeg(Guid.NewGuid(), Guid.NewGuid(), 2, 20.0);

        job.Legs.Should().HaveCount(2);
        job.EstimatedDistance.Should().Be(30.0);
    }

    // ── Phase 2 wiring: TransportMode / SlaDeadline / RequiredCapability ──

    [Fact]
    public void NewJob_TransportMode_IsNull()
    {
        var job = new Job(Guid.NewGuid(), "Normal");

        job.TransportMode.Should().BeNull();
        job.SlaDeadline.Should().BeNull();
        job.RequiredCapability.Should().BeNull();
    }

    [Fact]
    public void SetTransportMode_PersistsValue()
    {
        var job = new Job(Guid.NewGuid(), "Normal");

        job.SetTransportMode("Amr");

        job.TransportMode.Should().Be("Amr");
    }

    [Fact]
    public void SetSlaDeadline_PersistsValue()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        var deadline = DateTime.UtcNow.AddHours(8);

        job.SetSlaDeadline(deadline);

        job.SlaDeadline.Should().Be(deadline);
    }

    [Fact]
    public void SetRequiredCapability_PersistsValue()
    {
        var job = new Job(Guid.NewGuid(), "Normal");

        job.SetRequiredCapability("Hazmat");

        job.RequiredCapability.Should().Be("Hazmat");
    }

    // ── Phase b8: envelope-dispatch anchor (1:1 Job per station-pair group) ──

    [Fact]
    public void SetEnvelopeAnchor_PersistsGroupAndStations()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();

        job.SetEnvelopeAnchor(2, pickup, drop);

        job.GroupIndex.Should().Be(2);
        job.PickupStationId.Should().Be(pickup);
        job.DropStationId.Should().Be(drop);
    }

    [Fact]
    public void SetEnvelopeAnchor_RejectsZeroGroupIndex()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        Action act = () => job.SetEnvelopeAnchor(0, Guid.NewGuid(), Guid.NewGuid());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkDispatched_FromCreated_FlipsStatusAndLinksTrip()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        var tripId = Guid.NewGuid();

        job.MarkDispatched(tripId, "RIOT-KEY-1");

        job.Status.Should().Be(JobStatus.Dispatched);
        job.TripId.Should().Be(tripId);
        job.VendorOrderKey.Should().Be("RIOT-KEY-1");
        job.FailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkDispatched_FromNonCreated_Throws()
    {
        // Once a job is past Created (Assigned/Committed/Failed/Dispatched)
        // MarkDispatched is no longer valid — the operator path is Retry().
        var job = new Job(Guid.NewGuid(), "Normal");
        job.MarkFailed("template missing", JobFailureCategory.TemplateMissing);

        Action act = () => job.MarkDispatched(Guid.NewGuid(), "K");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkFailed_FromCreated_RecordsReason()
    {
        var job = new Job(Guid.NewGuid(), "Normal");

        job.MarkFailed("RIOT3 returned 429", JobFailureCategory.VendorRateLimited);

        job.Status.Should().Be(JobStatus.Failed);
        job.FailureReason.Should().Be("RIOT3 returned 429");
        job.FailureCategory.Should().Be(JobFailureCategory.VendorRateLimited);
    }

    [Fact]
    public void MarkFailed_FromDispatched_AllowedPhaseB9()
    {
        // Phase b9 relaxed MarkFailed so the vendor-side TripFailed
        // webhook can flip a Dispatched (not-yet-Started) Job to Failed
        // mid-flight without forcing a Retry() round-trip.
        var job = new Job(Guid.NewGuid(), "Normal");
        job.MarkDispatched(Guid.NewGuid(), "K");

        job.MarkFailed("vendor reported error before start", JobFailureCategory.VendorExecutionFailed);

        job.Status.Should().Be(JobStatus.Failed);
        job.FailureReason.Should().Be("vendor reported error before start");
    }

    [Fact]
    public void Retry_FromFailed_ResetsAndBumpsAttempt()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        // Simulate an orphan: MarkFailed after a TripId was set conceptually
        // by MarkDispatched would not be valid (FromDispatched throws), so
        // here we just go Failed-from-Created to mimic vendor reject.
        job.MarkFailed("template missing", JobFailureCategory.TemplateMissing);
        var beforeAttempt = job.AttemptNumber;

        var (previousTripId, newAttempt) = job.Retry();

        job.Status.Should().Be(JobStatus.Created);
        job.FailureReason.Should().BeNull();
        job.TripId.Should().BeNull();
        job.VendorOrderKey.Should().BeNull();
        job.AttemptNumber.Should().Be(beforeAttempt + 1);
        newAttempt.Should().Be(job.AttemptNumber);
        previousTripId.Should().BeNull();
    }

    [Fact]
    public void Retry_FromNonFailed_Throws()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        // Created → can't retry, nothing has failed yet
        Action act = () => job.Retry();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Retry_AfterDispatched_Throws()
    {
        // Successfully dispatched jobs use Trip-level retry, not Job-level.
        var job = new Job(Guid.NewGuid(), "Normal");
        job.MarkDispatched(Guid.NewGuid(), "K");

        Action act = () => job.Retry();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RetryThenMarkDispatched_LinksNewTrip()
    {
        // Full retry cycle: fail → retry → dispatch.
        var job = new Job(Guid.NewGuid(), "Normal");
        job.MarkFailed("vendor 503", JobFailureCategory.VendorRejected);

        job.Retry();
        job.AttemptNumber.Should().Be(2);

        var newTrip = Guid.NewGuid();
        job.MarkDispatched(newTrip, "K2");

        job.Status.Should().Be(JobStatus.Dispatched);
        job.TripId.Should().Be(newTrip);
        job.VendorOrderKey.Should().Be("K2");
        job.AttemptNumber.Should().Be(2, "AttemptNumber stays at the retry count once dispatched");
    }

    // ── Phase b9: Trip lifecycle mirrors onto Job ──

    private static Job Dispatched()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.MarkDispatched(Guid.NewGuid(), "K");
        return job;
    }

    [Fact]
    public void MarkExecuting_FromDispatched_FlipsStatus()
    {
        var job = Dispatched();
        var tripId = Guid.NewGuid();
        job.MarkExecuting(tripId);
        job.Status.Should().Be(JobStatus.Executing);
    }

    [Fact]
    public void MarkExecuting_FromCreated_Throws()
    {
        // Trip can't start before we've dispatched it.
        var job = new Job(Guid.NewGuid(), "Normal");
        Action act = () => job.MarkExecuting(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkExecuting_WhenAlreadyExecuting_IsNoOp()
    {
        // Webhook redelivery — second TripStarted shouldn't throw or
        // produce a stale-state error.
        var job = Dispatched();
        job.MarkExecuting(Guid.NewGuid());
        Action act = () => job.MarkExecuting(Guid.NewGuid());
        act.Should().NotThrow();
        job.Status.Should().Be(JobStatus.Executing);
    }

    [Fact]
    public void MarkExecuting_AfterCompleted_DoesNotRegress()
    {
        // Out-of-order webhook: TripStarted arrives after TripCompleted.
        // The Job has already moved to a terminal-positive state — don't
        // drag it back into Executing.
        var job = Dispatched();
        job.MarkCompleted(Guid.NewGuid());
        Action act = () => job.MarkExecuting(Guid.NewGuid());
        act.Should().NotThrow();
        job.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public void MarkCompleted_FromDispatched_SkipsExecuting()
    {
        // Some vendors may deliver TripCompleted before TripStarted ever
        // shows up. Job must accept the terminal directly.
        var job = Dispatched();
        var tripId = Guid.NewGuid();
        job.MarkCompleted(tripId);
        job.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public void MarkCompleted_FromExecuting_FlipsTerminal()
    {
        var job = Dispatched();
        job.MarkExecuting(Guid.NewGuid());
        job.MarkCompleted(Guid.NewGuid());
        job.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public void MarkCompleted_WhenAlreadyCompleted_IsNoOp()
    {
        var job = Dispatched();
        job.MarkCompleted(Guid.NewGuid());
        Action act = () => job.MarkCompleted(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void MarkFailed_FromExecuting_NowAllowed_PhaseB9()
    {
        // Phase b9 relaxes MarkFailed so the vendor-side TripFailed webhook
        // can flip a running Job to Failed mid-flight.
        var job = Dispatched();
        job.MarkExecuting(Guid.NewGuid());
        job.MarkFailed("vendor reported FAILED", JobFailureCategory.VendorExecutionFailed);
        job.Status.Should().Be(JobStatus.Failed);
        job.FailureReason.Should().Be("vendor reported FAILED");
    }

    [Fact]
    public void MarkCancelled_FromDispatched_FlipsTerminal()
    {
        // Matches the real E2E from earlier verification: RIOT3 cancels
        // before the trip ever started, so Job is still Dispatched.
        var job = Dispatched();
        var tripId = Guid.NewGuid();
        job.MarkCancelled(tripId, "E700001");
        job.Status.Should().Be(JobStatus.Cancelled);
        job.FailureReason.Should().Be("E700001");
        // Phase b13 — cancellation is fixed-category (operator-initiated).
        job.FailureCategory.Should().Be(JobFailureCategory.OperatorCancelled);
    }

    [Fact]
    public void NewJob_FailureCategory_DefaultsToNone()
    {
        // Phase b13 — fresh job before any MarkFailed/MarkCancelled
        // call must report None so the BI fact table's pre-b13 rows
        // (defaulted via migration) round-trip cleanly through the
        // domain mapper.
        var job = new Job(Guid.NewGuid(), "Normal");
        job.FailureCategory.Should().Be(JobFailureCategory.None);
    }

    [Fact]
    public void MarkCancelled_AfterCompleted_DoesNotRegress()
    {
        // Late cancellation webhook on a completed trip stays a no-op —
        // don't let a network reordering flip a happy job negative.
        var job = Dispatched();
        job.MarkCompleted(Guid.NewGuid());
        job.MarkCancelled(Guid.NewGuid(), "race");
        job.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public void MarkCancelled_WhenAlreadyCancelled_IsNoOp()
    {
        var job = Dispatched();
        job.MarkCancelled(Guid.NewGuid(), "E700001");
        Action act = () => job.MarkCancelled(Guid.NewGuid(), "again");
        act.Should().NotThrow();
    }

    [Fact]
    public void Retry_FromCancelled_Throws()
    {
        // User decision: Cancelled is terminal-intentional. Operator must
        // file a new DeliveryOrder to re-attempt, not /retry the Job.
        var job = Dispatched();
        job.MarkCancelled(Guid.NewGuid(), "operator abort");
        Action act = () => job.Retry();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RebindToRetryTrip_FromFailed_BumpsAttemptAndBindsNewTrip()
    {
        // Phase b9 — Trip-level retry path. The Job was already marked
        // Failed by the TripFailedJobConsumer; the new Trip arrives via
        // PlanningTripRetryDispatcher and rebinds the Job.
        var job = Dispatched();
        job.MarkExecuting(Guid.NewGuid());
        job.MarkFailed("vendor error", JobFailureCategory.VendorExecutionFailed);
        var beforeAttempt = job.AttemptNumber;

        var newTrip = Guid.NewGuid();
        job.RebindToRetryTrip(newTrip, "RIOT-NEW");

        job.Status.Should().Be(JobStatus.Dispatched);
        job.TripId.Should().Be(newTrip);
        job.VendorOrderKey.Should().Be("RIOT-NEW");
        job.AttemptNumber.Should().Be(beforeAttempt + 1);
        job.FailureReason.Should().BeNull();
    }

    [Fact]
    public void RebindToRetryTrip_FromCancelled_Allowed()
    {
        // Different from Retry() — the Dispatch-side ReissueTrip command
        // already validated operator intent, so the Job follows whether
        // it was Failed or Cancelled.
        var job = Dispatched();
        job.MarkCancelled(Guid.NewGuid(), "E700001");

        Action act = () => job.RebindToRetryTrip(Guid.NewGuid(), "K");

        act.Should().NotThrow();
        job.Status.Should().Be(JobStatus.Dispatched);
    }

    [Fact]
    public void RebindToRetryTrip_FromExecuting_Throws()
    {
        // Trip-level retry only makes sense when the previous Trip is
        // terminal. A running Trip can't be retried — operator should
        // cancel it first.
        var job = Dispatched();
        job.MarkExecuting(Guid.NewGuid());
        Action act = () => job.RebindToRetryTrip(Guid.NewGuid(), "K");
        act.Should().Throw<InvalidOperationException>();
    }
}

public class ActionTemplateTests
{
    private static ActionTemplate New(string name = "Lift")
        => new(name, ActionCategory.Std, vendorActionId: 4, param0: 1, param1: 0);

    [Fact]
    public void Construct_TrimsNameAndAssignsDefaults()
    {
        // RIOT3 form values land verbatim — entity should normalize whitespace,
        // start active, and stamp CreatedAt at construction time.
        var t = new ActionTemplate("  Waiting_Confirm  ", ActionCategory.Std, 131, 0, 0);

        t.Name.Should().Be("Waiting_Confirm");
        t.ActionCategory.Should().Be(ActionCategory.Std);
        t.VendorActionId.Should().Be(131);
        t.Param0.Should().Be(0);
        t.Param1.Should().Be(0);
        t.ParamStr.Should().BeNull();
        t.IsActive.Should().BeTrue();
        t.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        t.ModifiedAt.Should().BeNull();
    }

    [Fact]
    public void Construct_RejectsEmptyName()
    {
        Action act = () => new ActionTemplate("   ", ActionCategory.Std, 4, 1, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_RejectsNameOver100Chars()
    {
        var longName = new string('x', 101);
        Action act = () => new ActionTemplate(longName, ActionCategory.Std, 4, 1, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_OverwritesParamsAndStampsModifiedAt()
    {
        var t = New();
        t.ModifiedAt.Should().BeNull();

        t.Update(actionCategory: ActionCategory.Act, vendorActionId: 4, param0: 2, param1: 0,
                 paramStr: "fragile");

        t.ActionCategory.Should().Be(ActionCategory.Act);
        t.Param0.Should().Be(2);
        t.ParamStr.Should().Be("fragile");
        t.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_TreatsBlankParamStrAsNull()
    {
        // Operators may type spaces in the UI; entity normalizes to null so
        // queries like "templates with no param_str" stay consistent.
        var t = New();
        t.Update(ActionCategory.Std, 4, 1, 0, paramStr: "   ");
        t.ParamStr.Should().BeNull();
    }

    [Fact]
    public void Rename_AllowsNewNameAndStampsModifiedAt()
    {
        var t = New("Lift");
        t.Rename("Lift_Heavy");

        t.Name.Should().Be("Lift_Heavy");
        t.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Construct_DefaultsActionType_WhenNotProvided()
    {
        // The common case: operators don't think about this field, and the
        // wire token "standardRobotsCustom" is what RIOT3 expects for
        // custom-handler missions packed into id/param0/param1.
        var t = new ActionTemplate("Lift", ActionCategory.Std, 4, 1, 0);
        t.ActionType.Should().Be("standardRobotsCustom");
    }

    [Fact]
    public void Construct_AcceptsCustomActionType()
    {
        var t = new ActionTemplate("Lift", ActionCategory.Std, 4, 1, 0,
            actionType: "seerCustom");
        t.ActionType.Should().Be("seerCustom");
    }

    [Fact]
    public void Update_BlankActionType_FallsBackToDefault()
    {
        // Empty string from the UI should collapse to the default rather than
        // being stored verbatim — otherwise dispatch would emit "" to RIOT3.
        var t = new ActionTemplate("Lift", ActionCategory.Std, 4, 1, 0,
            actionType: "seerCustom");
        t.Update(ActionCategory.Std, 4, 1, 0, paramStr: null, actionType: "  ");
        t.ActionType.Should().Be("standardRobotsCustom");
    }

    [Fact]
    public void DeactivateThenActivate_FlipsIsActive()
    {
        var t = New();
        t.IsActive.Should().BeTrue();

        t.Deactivate();
        t.IsActive.Should().BeFalse();
        var firstMod = t.ModifiedAt;
        firstMod.Should().NotBeNull();

        t.Activate();
        t.IsActive.Should().BeTrue();
        t.ModifiedAt.Should().BeAfter(firstMod!.Value);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsNoOp()
    {
        // Idempotency keeps the audit trail clean — bulk-deactivate scripts
        // shouldn't bump ModifiedAt for templates that were already off.
        var t = New();
        t.Deactivate();
        var mod1 = t.ModifiedAt;

        t.Deactivate();

        t.ModifiedAt.Should().Be(mod1);
    }
}

public class OrderTemplateTests
{
    private static OrderTemplateMission Move(int seq = 1, int mapId = 2, int stationId = 3)
        => OrderTemplateMission.CreateMove(seq, "agv", mapId, stationId);

    private static OrderTemplateMission ActInline(int seq = 2)
        => OrderTemplateMission.CreateActInline(seq, "agv", "standardRobotsCustom", "NONE",
            new[]
            {
                new MissionActionParameter("id", "4"),
                new MissionActionParameter("param0", "2"),
                new MissionActionParameter("param1", "0")
            });

    private static OrderTemplateMission ActByRef(int seq = 2, string name = "Drop")
        => OrderTemplateMission.CreateActByReference(seq, "agv", name);

    [Fact]
    public void Construct_StoresAllFieldsAndSequencesMissionsFromOne()
    {
        // Caller may supply missions in arbitrary order — entity must
        // canonicalize the sequence so persistence + dispatch read in order.
        var t = new OrderTemplate(
            name: "TEST", priority: 50,
            structureType: "sequence", transportOrderPriority: 50,
            missions: new[] { ActInline(99), Move(7) });

        t.Name.Should().Be("TEST");
        t.Priority.Should().Be(50);
        t.StructureType.Should().Be("sequence");
        t.TransportOrderPriority.Should().Be(50);
        t.IsActive.Should().BeTrue();
        t.Missions.Should().HaveCount(2);
        t.Missions[0].Sequence.Should().Be(1);
        t.Missions[1].Sequence.Should().Be(2);
        t.Missions[0].Type.Should().Be(MissionType.Move);
        t.Missions[1].Type.Should().Be(MissionType.Act);
    }

    [Fact]
    public void Construct_WithoutRouteFields_LeavesPickupAndDropNull()
    {
        var t = new OrderTemplate(
            name: "GENERIC", priority: 10,
            structureType: "sequence", transportOrderPriority: 10,
            missions: new[] { Move() });

        t.PickupStationId.Should().BeNull();
        t.DropStationId.Should().BeNull();
    }

    [Fact]
    public void Construct_WithRouteFields_PersistsBoth()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();

        var t = new OrderTemplate(
            name: "ROUTE-A-TO-B", priority: 10,
            structureType: "sequence", transportOrderPriority: 10,
            missions: new[] { Move() },
            pickupStationId: pickup,
            dropStationId: drop);

        t.PickupStationId.Should().Be(pickup);
        t.DropStationId.Should().Be(drop);
    }

    [Fact]
    public void Update_CanChangeRouteFields()
    {
        var t = new OrderTemplate(
            name: "ROUTE", priority: 10,
            structureType: "sequence", transportOrderPriority: 10,
            missions: new[] { Move() },
            pickupStationId: Guid.NewGuid(),
            dropStationId: Guid.NewGuid());

        var newPickup = Guid.NewGuid();
        var newDrop = Guid.NewGuid();
        t.Update(
            priority: 20, structureType: "sequence", transportOrderPriority: 20,
            missions: new[] { Move() },
            appointVehicleKey: null, appointVehicleName: null,
            appointVehicleGroupKey: null, appointVehicleGroupName: null,
            appointQueueWaitArea: null, description: null,
            pickupStationId: newPickup, dropStationId: newDrop);

        t.PickupStationId.Should().Be(newPickup);
        t.DropStationId.Should().Be(newDrop);
    }

    [Fact]
    public void Construct_TrimsAndNormalizesEmptyVehicleHintsToNull()
    {
        // RIOT3 sends "" for unset binding fields; entity must collapse to null
        // so consumers don't have to special-case.
        var t = new OrderTemplate(
            name: "TEST", priority: 10,
            structureType: "sequence", transportOrderPriority: 10,
            missions: new[] { Move() },
            appointVehicleKey: "  ",
            appointVehicleName: "",
            appointQueueWaitArea: "  ");

        t.AppointVehicleKey.Should().BeNull();
        t.AppointVehicleName.Should().BeNull();
        t.AppointQueueWaitArea.Should().BeNull();
    }

    [Fact]
    public void Construct_RejectsEmptyMissions()
    {
        Action act = () => new OrderTemplate(
            name: "TEST", priority: 10,
            structureType: "sequence", transportOrderPriority: 10,
            missions: Array.Empty<OrderTemplateMission>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_RejectsInvalidStructureType()
    {
        // RIOT3 spec only defines "sequence" and "tree" — anything else is
        // a typo that would silently break the dispatch later.
        Action act = () => new OrderTemplate(
            name: "TEST", priority: 10,
            structureType: "parallel", transportOrderPriority: 10,
            missions: new[] { Move() });
        act.Should().Throw<ArgumentException>().WithMessage("*sequence*tree*");
    }

    [Fact]
    public void Mission_MoveRequiresMapAndStation()
    {
        // Use case: dispatcher MUST know which physical destination to send
        // to RIOT3 — no fallback default.
        Action act = () => OrderTemplateMission.CreateMove(1, "", 2, 3);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mission_ActByReferenceRequiresTemplateName()
    {
        Action act = () => OrderTemplateMission.CreateActByReference(1, "agv", "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mission_ActInlineRequiresActionType()
    {
        Action act = () => OrderTemplateMission.CreateActInline(1, "agv", "  ", "NONE",
            new[] { new MissionActionParameter("id", "4") });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mission_ActByReferenceDefaultsBlockingTypeToNone()
    {
        var m = OrderTemplateMission.CreateActByReference(1, "agv", "Drop");
        m.BlockingType.Should().Be("NONE");
        m.ActionTemplateName.Should().Be("Drop");
        m.ActionType.Should().BeNull();
        m.ActionParameters.Should().BeNull();
    }

    [Fact]
    public void Update_ReplacesMissionsAndStampsModifiedAt()
    {
        var t = new OrderTemplate("TEST", 10, "sequence", 10, new[] { Move() });
        t.Update(
            priority: 99, structureType: "sequence", transportOrderPriority: 99,
            missions: new[] { Move(), ActInline() },
            appointVehicleKey: "robot-1", appointVehicleName: null,
            appointVehicleGroupKey: null, appointVehicleGroupName: null,
            appointQueueWaitArea: null, description: "Updated");

        t.Priority.Should().Be(99);
        t.Missions.Should().HaveCount(2);
        t.AppointVehicleKey.Should().Be("robot-1");
        t.Description.Should().Be("Updated");
        t.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void DeactivateThenActivate_FlipsIsActive()
    {
        var t = new OrderTemplate("TEST", 10, "sequence", 10, new[] { Move() });
        t.Deactivate();
        t.IsActive.Should().BeFalse();
        t.Activate();
        t.IsActive.Should().BeTrue();
    }
}

public class OrderTemplateResolverTests
{
    // In-memory IActionTemplateRepository so tests stay free of EF / Postgres.
    private sealed class StubActionRepo : AMR.DeliveryPlanning.Planning.Domain.Repositories.IActionTemplateRepository
    {
        private readonly Dictionary<string, ActionTemplate> _byName;
        public StubActionRepo(params ActionTemplate[] templates)
        {
            _byName = templates.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        }

        public Task<ActionTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<ActionTemplate?>(_byName.Values.FirstOrDefault(t => t.Id == id));

        public Task<ActionTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            _byName.TryGetValue(name, out var t);
            return Task.FromResult<ActionTemplate?>(t);
        }

        public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_byName.ContainsKey(name));

        public Task<(IReadOnlyList<ActionTemplate> Items, long Total)> ListPagedAsync(int page, int size, bool includeInactive = false, ActionCategory? actionCategory = null, string? search = null, string? sortBy = null, bool sortDescending = false, CancellationToken cancellationToken = default)
        {
            var all = _byName.Values.ToList();
            return Task.FromResult<(IReadOnlyList<ActionTemplate>, long)>((all, all.Count));
        }

        public Task<(int Total, int Active, int Std, int Act)> GetStatsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((0, 0, 0, 0));

        public Task AddAsync(ActionTemplate template, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(ActionTemplate template) { }
        public void Remove(ActionTemplate template) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Resolve_InlineActMissions_PassThroughUnchanged()
    {
        // Templates that ship inline params (paste-from-RIOT3 case) must
        // round-trip through the resolver without touching the catalog.
        var template = new OrderTemplate(
            name: "INLINE", priority: 50,
            structureType: "sequence", transportOrderPriority: 50,
            missions: new[]
            {
                OrderTemplateMission.CreateMove(1, "agv", 2, 3),
                OrderTemplateMission.CreateActInline(2, "agv", "standardRobotsCustom", "NONE",
                    new[] {
                        new MissionActionParameter("id", "4"),
                        new MissionActionParameter("param0", "2"),
                        new MissionActionParameter("param1", "0")
                    })
            });

        var resolver = new AMR.DeliveryPlanning.Planning.Application.Services.OrderTemplateResolver(
            new StubActionRepo());
        var resolved = await resolver.ResolveAsync(template);

        resolved.Missions.Should().HaveCount(2);
        resolved.Missions[0].Type.Should().Be("MOVE");
        resolved.Missions[0].MapId.Should().Be(2);
        resolved.Missions[0].StationId.Should().Be(3);

        resolved.Missions[1].Type.Should().Be("ACT");
        resolved.Missions[1].ActionType.Should().Be("standardRobotsCustom");
        resolved.Missions[1].ActionParameters.Should().HaveCount(3);
        // Numeric strings re-typed as int so RIOT3 sees JSON numbers
        resolved.Missions[1].ActionParameters![0].Value.Should().Be(4);
        resolved.Missions[1].ActionParameters![1].Value.Should().Be(2);
        resolved.Missions[1].ActionParameters![2].Value.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_ActByReference_ExpandsFromCatalog()
    {
        // The whole point of the catalog: an ACT mission with just
        // actionTemplateName must come out the other side with the four
        // RIOT3 parameter slots populated from the named ActionTemplate.
        var lift = new ActionTemplate("Lift", ActionCategory.Std,
            vendorActionId: 4, param0: 1, param1: 0,
            paramStr: null);

        var template = new OrderTemplate(
            name: "REF", priority: 10,
            structureType: "sequence", transportOrderPriority: 10,
            missions: new[]
            {
                OrderTemplateMission.CreateMove(1, "agv", 2, 5),
                OrderTemplateMission.CreateActByReference(2, "agv", "Lift")
            });

        var resolver = new AMR.DeliveryPlanning.Planning.Application.Services.OrderTemplateResolver(
            new StubActionRepo(lift));
        var resolved = await resolver.ResolveAsync(template);

        var act = resolved.Missions[1];
        // Wire token now comes from ActionTemplate.ActionType, which
        // defaults to "standardRobotsCustom" — the value RIOT3 expects for
        // custom-handler missions packed into id/param0/param1.
        act.ActionType.Should().Be("standardRobotsCustom");
        act.BlockingType.Should().Be("NONE");
        act.ActionParameters.Should().HaveCount(3,
            "ParamStr was null on the ActionTemplate so the resolver omits the param_str slot");
        act.ActionParameters![0].Key.Should().Be("id");
        act.ActionParameters[0].Value.Should().Be(4);
        act.ActionParameters[1].Key.Should().Be("param0");
        act.ActionParameters[2].Key.Should().Be("param1");
    }

    [Fact]
    public async Task Resolve_ActByReference_IncludesParamStrWhenSet()
    {
        var fancy = new ActionTemplate("Fancy", ActionCategory.Std,
            vendorActionId: 7, param0: 0, param1: 0,
            paramStr: "label-A");

        var template = new OrderTemplate(
            name: "REF_STR", priority: 1,
            structureType: "sequence", transportOrderPriority: 1,
            missions: new[] { OrderTemplateMission.CreateActByReference(1, "agv", "Fancy") });

        var resolver = new AMR.DeliveryPlanning.Planning.Application.Services.OrderTemplateResolver(
            new StubActionRepo(fancy));
        var resolved = await resolver.ResolveAsync(template);

        resolved.Missions[0].ActionParameters.Should().HaveCount(4);
        resolved.Missions[0].ActionParameters![3].Key.Should().Be("param_str");
        resolved.Missions[0].ActionParameters![3].Value.Should().Be("label-A");
    }

    [Fact]
    public async Task Resolve_UnknownActionTemplateName_Throws()
    {
        // Phase 1C validation already blocks this on save but the resolver
        // re-checks at runtime in case the catalog was edited after the
        // template was stored.
        var template = new OrderTemplate(
            name: "BAD", priority: 1,
            structureType: "sequence", transportOrderPriority: 1,
            missions: new[] { OrderTemplateMission.CreateActByReference(1, "agv", "Missing") });

        var resolver = new AMR.DeliveryPlanning.Planning.Application.Services.OrderTemplateResolver(
            new StubActionRepo());

        var act = async () => await resolver.ResolveAsync(template);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'Missing' not found*");
    }

    [Fact]
    public async Task Resolve_SameRefUsedTwice_LooksUpOnlyOnce()
    {
        // Cache check — two references to the same ActionTemplate in one
        // template must hit the repository only once. Counts the calls to
        // a tiny counting stub.
        var lift = new ActionTemplate("Lift", ActionCategory.Std, 4, 1, 0);
        var counter = new CountingRepo(lift);

        var template = new OrderTemplate(
            name: "DOUBLE", priority: 1,
            structureType: "sequence", transportOrderPriority: 1,
            missions: new[]
            {
                OrderTemplateMission.CreateActByReference(1, "agv", "Lift"),
                OrderTemplateMission.CreateActByReference(2, "agv", "Lift")
            });

        var resolver = new AMR.DeliveryPlanning.Planning.Application.Services.OrderTemplateResolver(counter);
        await resolver.ResolveAsync(template);

        counter.GetByNameCalls.Should().Be(1);
    }

    private sealed class CountingRepo : AMR.DeliveryPlanning.Planning.Domain.Repositories.IActionTemplateRepository
    {
        public int GetByNameCalls { get; private set; }
        private readonly ActionTemplate _template;
        public CountingRepo(ActionTemplate t) => _template = t;
        public Task<ActionTemplate?> GetByIdAsync(Guid id, CancellationToken c = default) => Task.FromResult<ActionTemplate?>(null);
        public Task<ActionTemplate?> GetByNameAsync(string name, CancellationToken c = default)
        {
            GetByNameCalls++;
            return Task.FromResult<ActionTemplate?>(string.Equals(name, _template.Name, StringComparison.OrdinalIgnoreCase) ? _template : null);
        }
        public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken c = default) => Task.FromResult(true);
        public Task<(IReadOnlyList<ActionTemplate> Items, long Total)> ListPagedAsync(int page, int size, bool includeInactive = false, ActionCategory? actionCategory = null, string? search = null, string? sortBy = null, bool sortDescending = false, CancellationToken c = default)
            => Task.FromResult<(IReadOnlyList<ActionTemplate>, long)>((new[] { _template }, 1));
        public Task<(int Total, int Active, int Std, int Act)> GetStatsAsync(CancellationToken c = default)
            => Task.FromResult((1, 1, 1, 0));
        public Task AddAsync(ActionTemplate t, CancellationToken c = default) => Task.CompletedTask;
        public void Update(ActionTemplate t) { }
        public void Remove(ActionTemplate t) { }
        public Task SaveChangesAsync(CancellationToken c = default) => Task.CompletedTask;
    }
}

public class EnvelopeUpperKeyTests
{
    [Fact]
    public void Build_FormatsAsGuidNHyphenGN()
    {
        var orderId = Guid.Parse("48752c3e-35bb-4d0d-b227-cbde6c1da95b");
        var key = AMR.DeliveryPlanning.SharedKernel.EnvelopeUpperKey.Build(orderId, 1);
        key.Should().Be("48752c3e35bb4d0db227cbde6c1da95b-G1");
    }

    [Fact]
    public void TryParse_ValidComposite_ReturnsParts()
    {
        var ok = AMR.DeliveryPlanning.SharedKernel.EnvelopeUpperKey.TryParse(
            "48752c3e35bb4d0db227cbde6c1da95b-G3", out var orderId, out var groupIndex);
        ok.Should().BeTrue();
        orderId.Should().Be(Guid.Parse("48752c3e-35bb-4d0d-b227-cbde6c1da95b"));
        groupIndex.Should().Be(3);
    }

    [Fact]
    public void TryParse_PlainGuid_ReturnsFalse()
    {
        // legacy upperKey (plain Guid) should NOT match envelope format —
        // webhook needs this so it falls through to the legacy branch.
        var ok = AMR.DeliveryPlanning.SharedKernel.EnvelopeUpperKey.TryParse(
            "48752c3e-35bb-4d0d-b227-cbde6c1da95b", out _, out _);
        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("48752c3e35bb4d0db227cbde6c1da95b")]      // no group suffix
    [InlineData("48752c3e35bb4d0db227cbde6c1da95b-X1")]   // wrong separator
    [InlineData("zzzzz35bb4d0db227cbde6c1da95b-G1")]      // bad hex
    public void TryParse_Invalid_ReturnsFalse(string? input)
    {
        AMR.DeliveryPlanning.SharedKernel.EnvelopeUpperKey
            .TryParse(input, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void RoundTrip()
    {
        var orderId = Guid.NewGuid();
        var key = AMR.DeliveryPlanning.SharedKernel.EnvelopeUpperKey.Build(orderId, 7);
        AMR.DeliveryPlanning.SharedKernel.EnvelopeUpperKey.TryParse(key, out var roundOrder, out var roundGroup)
            .Should().BeTrue();
        roundOrder.Should().Be(orderId);
        roundGroup.Should().Be(7);
    }
}

public class DispatchOrderTemplateServiceTests
{
    private static OrderTemplate TemplateFor(Guid pickup, Guid drop, string name = "ROUTE")
        => new(
            name: name,
            priority: 10,
            structureType: "sequence",
            transportOrderPriority: 10,
            missions: new[] { OrderTemplateMission.CreateMove(1, "agv", 27, 10) },
            pickupStationId: pickup,
            dropStationId: drop);

    [Fact]
    public async Task DispatchByRoute_NoTemplate_ReturnsFailure()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var repo = new StubTemplateRepo(matching: null);
        var resolver = new StubResolver();
        var dispatcher = new StubDispatcher();

        var svc = new AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService(
            repo, resolver, dispatcher, new StubSender(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService>.Instance);

        var result = await svc.DispatchByRouteAsync(Guid.NewGuid(), pickup, drop, upperKey: "trip-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("No active OrderTemplate");
        dispatcher.Called.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchByRoute_EmptyUpperKey_ReturnsFailure()
    {
        var svc = new AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService(
            new StubTemplateRepo(matching: null),
            new StubResolver(),
            new StubDispatcher(),
            new StubSender(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService>.Instance);

        var result = await svc.DispatchByRouteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), upperKey: "  ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("UpperKey is required");
    }

    [Fact]
    public async Task DispatchByRoute_HappyPath_SendsResolvedToDispatcher()
    {
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var template = TemplateFor(pickup, drop, "WH-A_to_Pack-1");
        var dispatcher = new StubDispatcher { ReturnVendorKey = "RIOT-ORDER-123" };

        var svc = new AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService(
            new StubTemplateRepo(matching: template),
            new StubResolver(),
            dispatcher,
            new StubSender(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService>.Instance);

        var result = await svc.DispatchByRouteAsync(Guid.NewGuid(), pickup, drop, upperKey: "trip-abc");

        result.IsSuccess.Should().BeTrue();
        result.Value.OrderTemplateId.Should().Be(template.Id);
        result.Value.TemplateName.Should().Be("WH-A_to_Pack-1");
        result.Value.VendorOrderKey.Should().Be("RIOT-ORDER-123");
        dispatcher.LastUpperKey.Should().Be("trip-abc");
        dispatcher.LastSentOrder.Should().NotBeNull();
    }

    [Fact]
    public async Task DispatchByRoute_AppliesPriorityOverride()
    {
        var template = TemplateFor(Guid.NewGuid(), Guid.NewGuid());
        var dispatcher = new StubDispatcher { ReturnVendorKey = "K" };

        var svc = new AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService(
            new StubTemplateRepo(matching: template),
            new StubResolver(),
            dispatcher,
            new StubSender(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService>.Instance);

        await svc.DispatchByRouteAsync(Guid.NewGuid(), template.PickupStationId!.Value, template.DropStationId!.Value,
            upperKey: "trip", priorityOverride: 99);

        dispatcher.LastSentOrder!.Priority.Should().Be(99);
    }

    [Fact]
    public async Task DispatchByRoute_BlankOverrides_KeepTemplateDefault()
    {
        var template = TemplateFor(Guid.NewGuid(), Guid.NewGuid());
        var resolver = new StubResolver { AppointVehicleKey = "robot-7" };
        var dispatcher = new StubDispatcher { ReturnVendorKey = "K" };

        var svc = new AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService(
            new StubTemplateRepo(matching: template),
            resolver,
            dispatcher,
            new StubSender(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService>.Instance);

        await svc.DispatchByRouteAsync(Guid.NewGuid(), template.PickupStationId!.Value, template.DropStationId!.Value,
            upperKey: "trip", appointVehicleKeyOverride: "   ");

        dispatcher.LastSentOrder!.AppointVehicleKey.Should().Be("robot-7");
    }

    [Fact]
    public async Task DispatchByRoute_VendorRejects_ReturnsFailure()
    {
        var template = TemplateFor(Guid.NewGuid(), Guid.NewGuid());
        var dispatcher = new StubDispatcher { FailWith = "vendor rejected: capacity" };

        var svc = new AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService(
            new StubTemplateRepo(matching: template),
            new StubResolver(),
            dispatcher,
            new StubSender(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AMR.DeliveryPlanning.Planning.Application.Services.DispatchOrderTemplateService>.Instance);

        var result = await svc.DispatchByRouteAsync(Guid.NewGuid(), template.PickupStationId!.Value, template.DropStationId!.Value, upperKey: "trip");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("vendor rejected");
    }

    private sealed class StubTemplateRepo : AMR.DeliveryPlanning.Planning.Domain.Repositories.IOrderTemplateRepository
    {
        private readonly OrderTemplate? _matching;
        public StubTemplateRepo(OrderTemplate? matching) => _matching = matching;
        public Task<OrderTemplate?> FindByRouteAsync(Guid pickupStationId, Guid dropStationId, CancellationToken c = default)
            => Task.FromResult(_matching);
        public Task<OrderTemplate?> GetByIdAsync(Guid id, CancellationToken c = default) => Task.FromResult<OrderTemplate?>(null);
        public Task<OrderTemplate?> GetByNameAsync(string name, CancellationToken c = default) => Task.FromResult<OrderTemplate?>(null);
        public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken c = default) => Task.FromResult(false);
        public Task<(IReadOnlyList<OrderTemplate> Items, long Total)> ListPagedAsync(int page, int size, bool includeInactive = false, CancellationToken c = default)
            => Task.FromResult<(IReadOnlyList<OrderTemplate>, long)>((Array.Empty<OrderTemplate>(), 0));
        public Task AddAsync(OrderTemplate t, CancellationToken c = default) => Task.CompletedTask;
        public void Update(OrderTemplate t) { }
        public void Remove(OrderTemplate t) { }
        public Task SaveChangesAsync(CancellationToken c = default) => Task.CompletedTask;
    }

    private sealed class StubResolver : AMR.DeliveryPlanning.Planning.Application.Services.IOrderTemplateResolver
    {
        public string? AppointVehicleKey { get; set; }
        public Task<AMR.DeliveryPlanning.Planning.Application.Services.ResolvedOrder> ResolveAsync(
            OrderTemplate template, CancellationToken c = default)
        {
            var resolved = new AMR.DeliveryPlanning.Planning.Application.Services.ResolvedOrder(
                Name: template.Name,
                Priority: template.Priority,
                StructureType: template.StructureType,
                TransportOrderPriority: template.TransportOrderPriority,
                Missions: Array.Empty<AMR.DeliveryPlanning.Planning.Application.Services.ResolvedMission>(),
                AppointVehicleKey: AppointVehicleKey ?? template.AppointVehicleKey,
                AppointVehicleName: template.AppointVehicleName,
                AppointVehicleGroupKey: template.AppointVehicleGroupKey,
                AppointVehicleGroupName: template.AppointVehicleGroupName,
                AppointQueueWaitArea: template.AppointQueueWaitArea);
            return Task.FromResult(resolved);
        }
    }

    // Minimal MediatR ISender stub — DispatchOrderTemplateService uses it
    // to send CreateEnvelopeTripCommand into the Dispatch module. Tests
    // DispatchOrderTemplateService now sends two commands:
    //   CreateEnvelopeTripCommand   → Result<Guid>
    //   AssignItemsToTripCommand    → Result<int>
    // Branch on the response generic so each test target sees a valid result.
    private sealed class StubSender : MediatR.ISender
    {
        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response;
            if (typeof(TResponse) == typeof(AMR.DeliveryPlanning.SharedKernel.Messaging.Result<Guid>))
                response = AMR.DeliveryPlanning.SharedKernel.Messaging.Result<Guid>.Success(Guid.NewGuid());
            else if (typeof(TResponse) == typeof(AMR.DeliveryPlanning.SharedKernel.Messaging.Result<int>))
                response = AMR.DeliveryPlanning.SharedKernel.Messaging.Result<int>.Success(0);
            else
                throw new NotSupportedException($"StubSender doesn't model {typeof(TResponse).Name}.");
            return Task.FromResult((TResponse)response);
        }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : MediatR.IRequest
            => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubDispatcher : AMR.DeliveryPlanning.Planning.Application.Services.IRobotOrderDispatcher
    {
        public bool Called { get; private set; }
        public string? LastUpperKey { get; private set; }
        public AMR.DeliveryPlanning.Planning.Application.Services.ResolvedOrder? LastSentOrder { get; private set; }
        public string? ReturnVendorKey { get; set; }
        public string? FailWith { get; set; }

        public Task<AMR.DeliveryPlanning.SharedKernel.Messaging.Result<
            AMR.DeliveryPlanning.Planning.Application.Services.RobotOrderDispatchResult>> SendAsync(
            string upperKey,
            AMR.DeliveryPlanning.Planning.Application.Services.ResolvedOrder order,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            LastUpperKey = upperKey;
            LastSentOrder = order;
            return Task.FromResult(FailWith is not null
                ? AMR.DeliveryPlanning.SharedKernel.Messaging.Result<
                    AMR.DeliveryPlanning.Planning.Application.Services.RobotOrderDispatchResult>.Failure(FailWith)
                : AMR.DeliveryPlanning.SharedKernel.Messaging.Result<
                    AMR.DeliveryPlanning.Planning.Application.Services.RobotOrderDispatchResult>.Success(
                        new AMR.DeliveryPlanning.Planning.Application.Services.RobotOrderDispatchResult(
                            ReturnVendorKey!, "{\"stub\":true}")));
        }
    }
}
