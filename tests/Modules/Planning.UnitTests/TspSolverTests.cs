using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Planning.UnitTests;

public class TspSolverTests
{
    private readonly SimpleRouteCostCalculator _costCalc = new(NullLogger<SimpleRouteCostCalculator>.Instance);

    [Fact]
    public void SingleDrop_ReturnsSameStation()
    {
        var solver = new NearestNeighborTspSolver(_costCalc, NullLogger<NearestNeighborTspSolver>.Instance);
        var start = Guid.NewGuid();
        var drop = Guid.NewGuid();

        var route = solver.SolveRoute(start, new List<Guid> { drop });

        route.Should().HaveCount(1);
        route[0].Should().Be(drop);
    }

    [Fact]
    public void MultipleDrops_ReturnsAllStations()
    {
        var solver = new NearestNeighborTspSolver(_costCalc, NullLogger<NearestNeighborTspSolver>.Instance);
        var start = Guid.NewGuid();
        var drops = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var route = solver.SolveRoute(start, drops);

        route.Should().HaveCount(4);
        route.Should().BeEquivalentTo(drops); // all stations visited
    }

    [Fact]
    public void EmptyDrops_ReturnsEmpty()
    {
        var solver = new NearestNeighborTspSolver(_costCalc, NullLogger<NearestNeighborTspSolver>.Instance);

        var route = solver.SolveRoute(Guid.NewGuid(), new List<Guid>());

        route.Should().BeEmpty();
    }
}
