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
}

public class ActionTemplateTests
{
    private static ActionTemplate New(string name = "Lift")
        => new(name, "SR action", vendorActionId: 4, param0: 1, param1: 0);

    [Fact]
    public void Construct_TrimsNameAndAssignsDefaults()
    {
        // RIOT3 form values land verbatim — entity should normalize whitespace,
        // start active, and stamp CreatedAt at construction time.
        var t = new ActionTemplate("  Waiting_Confirm  ", "SR action", 131, 0, 0);

        t.Name.Should().Be("Waiting_Confirm");
        t.ActionType.Should().Be("SR action");
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
        Action act = () => new ActionTemplate("   ", "SR action", 4, 1, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_RejectsEmptyActionType()
    {
        Action act = () => new ActionTemplate("Lift", " ", 4, 1, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_RejectsNameOver100Chars()
    {
        var longName = new string('x', 101);
        Action act = () => new ActionTemplate(longName, "SR action", 4, 1, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_OverwritesParamsAndStampsModifiedAt()
    {
        var t = New();
        t.ModifiedAt.Should().BeNull();

        t.Update(actionType: "SR action", vendorActionId: 4, param0: 2, param1: 0,
                 paramStr: "fragile", description: "Drop variant");

        t.Param0.Should().Be(2);
        t.ParamStr.Should().Be("fragile");
        t.Description.Should().Be("Drop variant");
        t.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_TreatsBlankParamStrAsNull()
    {
        // Operators may type spaces in the UI; entity normalizes to null so
        // queries like "templates with no param_str" stay consistent.
        var t = New();
        t.Update("SR action", 4, 1, 0, paramStr: "   ", description: null);
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
