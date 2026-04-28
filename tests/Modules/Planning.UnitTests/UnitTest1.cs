using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using FluentAssertions;

namespace Planning.UnitTests;

public class JobTests
{
    [Fact]
    public void NewJob_ShouldHaveCreatedStatus()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "High");

        job.Status.Should().Be(JobStatus.Created);
        job.Priority.Should().Be("High");
        job.AssignedVehicleId.Should().BeNull();
        job.Legs.Should().BeEmpty();
    }

    [Fact]
    public void AddLeg_ShouldAddToCollection()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");
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
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");
        var vehicleId = Guid.NewGuid();

        job.AssignVehicle(vehicleId, 120);

        job.Status.Should().Be(JobStatus.Assigned);
        job.AssignedVehicleId.Should().Be(vehicleId);
        job.EstimatedDuration.Should().Be(120);
    }

    [Fact]
    public void AssignVehicle_WhenNotCreated_ShouldThrow()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");
        job.AssignVehicle(Guid.NewGuid(), 60);

        var act = () => job.AssignVehicle(Guid.NewGuid(), 60);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Commit_ShouldChangeStatusToCommitted()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");
        job.AssignVehicle(Guid.NewGuid(), 60);

        job.Commit();

        job.Status.Should().Be(JobStatus.Committed);
    }

    [Fact]
    public void Commit_WhenNotAssigned_ShouldThrow()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");

        var act = () => job.Commit();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddMultipleLegs_ShouldAccumulateDistance()
    {
        var job = new Job(Guid.Empty, Guid.NewGuid(), "Normal");

        job.AddLeg(Guid.NewGuid(), Guid.NewGuid(), 1, 10.0);
        job.AddLeg(Guid.NewGuid(), Guid.NewGuid(), 2, 20.0);

        job.Legs.Should().HaveCount(2);
        job.EstimatedDistance.Should().Be(30.0);
    }
}
