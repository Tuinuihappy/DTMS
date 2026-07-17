using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using FluentAssertions;

namespace Planning.UnitTests;

public class Phase3DomainTests
{
    // ── Cross-Dock ──

    [Fact]
    public void JobDependency_CreatesLinkBetweenJobs()
    {
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();

        var dep = new JobDependency(predId, succId, "CROSS_DOCK", TimeSpan.FromMinutes(5));

        dep.PredecessorJobId.Should().Be(predId);
        dep.SuccessorJobId.Should().Be(succId);
        dep.DependencyType.Should().Be("CROSS_DOCK");
        dep.MinimumDwell.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void CrossDockJob_HasCorrectPattern()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.SetPattern(PatternType.CrossDock);

        job.Pattern.Should().Be(PatternType.CrossDock);
    }

    // ── Milk-Run ──
    // (MilkRunTemplate entity tests removed 2026-07-17 with the legacy
    // manual-planning stack; PatternType.MilkRun on Job remains valid.)

    [Fact]
    public void MilkRunJob_HasCorrectPattern()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.SetPattern(PatternType.MilkRun);

        job.Pattern.Should().Be(PatternType.MilkRun);
    }

    // ── Multi-Pick Multi-Drop ──

    [Fact]
    public void MultiPickDropJob_HasCorrectPattern()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.SetPattern(PatternType.MultiPickMultiDrop);
        job.SetTotalWeight(45.5);

        job.Pattern.Should().Be(PatternType.MultiPickMultiDrop);
        job.TotalWeight.Should().Be(45.5);
    }

    [Fact]
    public void MultiPickDropJob_MultipleLegs_MaintainOrder()
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var s3 = Guid.NewGuid();
        var s4 = Guid.NewGuid();

        // Pair 1: pick(s1) → drop(s2)
        job.AddLeg(Guid.Empty, s1, 1, 10);
        job.AddLeg(s1, s2, 2, 15);
        // Pair 2: pick(s3) → drop(s4)
        job.AddLeg(s2, s3, 3, 12);
        job.AddLeg(s3, s4, 4, 18);

        job.Legs.Should().HaveCount(4);
        job.Legs.Select(l => l.SequenceOrder).Should().BeInAscendingOrder();
    }
}
