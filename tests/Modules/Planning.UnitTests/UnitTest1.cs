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

        public Task<IReadOnlyList<ActionTemplate>> ListAsync(bool includeInactive = false, string? actionType = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ActionTemplate>>(_byName.Values.ToList());

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
        var lift = new ActionTemplate("Lift", "SR action",
            vendorActionId: 4, param0: 1, param1: 0,
            paramStr: null, description: null);

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
        act.ActionType.Should().Be("SR action");
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
        var fancy = new ActionTemplate("Fancy", "SR action",
            vendorActionId: 7, param0: 0, param1: 0,
            paramStr: "label-A", description: null);

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
        var lift = new ActionTemplate("Lift", "SR action", 4, 1, 0);
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
        public Task<IReadOnlyList<ActionTemplate>> ListAsync(bool includeInactive = false, string? actionType = null, CancellationToken c = default)
            => Task.FromResult<IReadOnlyList<ActionTemplate>>(new[] { _template });
        public Task AddAsync(ActionTemplate t, CancellationToken c = default) => Task.CompletedTask;
        public void Update(ActionTemplate t) { }
        public void Remove(ActionTemplate t) { }
        public Task SaveChangesAsync(CancellationToken c = default) => Task.CompletedTask;
    }
}
