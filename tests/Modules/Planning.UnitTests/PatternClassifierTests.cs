using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Planning.UnitTests;

public class PatternClassifierTests
{
    private readonly PatternClassifier _classifier = new(NullLogger<PatternClassifier>.Instance);

    [Fact]
    public void SingleOrder_SingleDrop_ReturnsPointToPoint()
    {
        var orders = new List<OrderInfo>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, null, null, 10, null)
        };

        var result = _classifier.Classify(orders);

        result.Pattern.Should().Be(PatternType.PointToPoint);
    }

    [Fact]
    public void SingleOrder_MultipleDrops_ReturnsMultiStop()
    {
        var orders = new List<OrderInfo>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }, null, null, 30, null)
        };

        var result = _classifier.Classify(orders);

        result.Pattern.Should().Be(PatternType.MultiStop);
    }

    [Fact]
    public void MultipleOrders_ReturnsConsolidation()
    {
        var orders = new List<OrderInfo>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, "ZoneA", DateTime.UtcNow.AddHours(2), 5, null),
            new(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, "ZoneA", DateTime.UtcNow.AddHours(3), 8, null),
            new(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, "ZoneA", DateTime.UtcNow.AddHours(4), 3, null)
        };

        var result = _classifier.Classify(orders);

        result.Pattern.Should().Be(PatternType.Consolidation);
        result.GroupedOrders.Should().HaveCount(3);
    }

    [Fact]
    public void EmptyOrders_Throws()
    {
        var act = () => _classifier.Classify(new List<OrderInfo>());
        act.Should().Throw<ArgumentException>();
    }
}
