using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using FluentAssertions;

namespace Planning.UnitTests;

public class JobReplanTests
{
    [Fact]
    public void Replan_CommittedJob_ResetsToCreated()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");
        job.AssignVehicle(Guid.NewGuid(), 30.0);
        job.Commit();

        job.Replan("DISRUPTION");

        job.Status.Should().Be(JobStatus.Created);
        job.AssignedVehicleId.Should().BeNull();
    }

    [Fact]
    public void Replan_AssignedJob_ResetsToCreated()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");
        job.AssignVehicle(Guid.NewGuid(), 30.0);

        job.Replan("PRIORITY_CHANGE");

        job.Status.Should().Be(JobStatus.Created);
    }

    [Fact]
    public void Replan_CreatedJob_Throws()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");

        var act = () => job.Replan("TEST");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConsolidationConstructor_SetsMultipleOrders()
    {
        var orderIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var job = new Job(Guid.Empty, orderIds, "High", PatternType.Consolidation);

        job.Pattern.Should().Be(PatternType.Consolidation);
        job.DerivedFromOrders.Should().HaveCount(3);
        job.DerivedFromOrders.Should().BeEquivalentTo(orderIds);
    }

    [Fact]
    public void SetPattern_UpdatesPattern()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");

        job.SetPattern(PatternType.MultiStop);

        job.Pattern.Should().Be(PatternType.MultiStop);
    }

    [Fact]
    public void SetRequiredCapability_UpdatesCapability()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");

        job.SetRequiredCapability("LIFT");

        job.RequiredCapability.Should().Be("LIFT");
    }
}
