using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Application.Queries.GetManualOperatorsReport;
using FluentAssertions;
using NSubstitute;

namespace Dispatch.UnitTests;

public class GetManualOperatorsReportQueryTests
{
    private static readonly DateTime From = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddDays(7);

    [Fact]
    public async Task AmrTrips_AreExcluded()
    {
        var result = await Run(
            Trip("Completed", "alice"),
            Trip("Completed", "alice", isManual: false));

        result.TotalTrips.Should().Be(1);
        result.Rows.Should().ContainSingle().Which.TotalTrips.Should().Be(1);
    }

    [Fact]
    public async Task OneRowPerOperator_AndEachRowAddsUp()
    {
        var result = await Run(
            Trip("Completed", "alice"),
            Trip("Cancelled", "alice"),
            Trip("InProgress", "alice"),
            Trip("Failed", "bob"),
            Trip("Rejected", "bob"));

        result.Rows.Select(r => r.Operator).Should().Equal("alice", "bob");
        foreach (var r in result.Rows)
            (r.Completed + r.Failed + r.Rejected + r.Cancelled + r.InProgress)
                .Should().Be(r.TotalTrips);

        var alice = result.Rows[0];
        alice.InProgress.Should().Be(1);
        alice.SuccessRate.Should().Be(0.5); // 1 completed of 2 terminal
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingAcknowledger_LandsInNotAcknowledged(string? acknowledgedBy)
    {
        var result = await Run(Trip("Completed", acknowledgedBy));

        result.Rows.Should().ContainSingle()
            .Which.Operator.Should().Be(GetManualOperatorsReportQueryHandler.NotAcknowledged);
    }

    [Fact]
    public async Task Median_IsTheMiddleSample()
    {
        var result = await Run(
            Trip("Completed", "alice", timeToCompleteSec: 600),
            Trip("Completed", "alice", timeToCompleteSec: 60),
            Trip("Completed", "alice", timeToCompleteSec: 120));

        result.Rows[0].MedianTimeToCompleteSec.Should().Be(120);
        result.Rows[0].P95TimeToCompleteSec.Should().Be(600);
    }

    [Fact]
    public async Task WindowOver90Days_Fails()
    {
        var handler = new GetManualOperatorsReportQueryHandler(Substitute.For<ITripFactsReadRepository>());

        var result = await handler.Handle(
            new GetManualOperatorsReportQuery(From, From.AddDays(91)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private static async Task<ManualOperatorsResponse> Run(params TripFactsEntry[] trips)
    {
        var repo = Substitute.For<ITripFactsReadRepository>();
        repo.QueryAsync(Arg.Any<TripFactsFilters>(), Arg.Any<CancellationToken>())
            .Returns(trips);
        var result = await new GetManualOperatorsReportQueryHandler(repo)
            .Handle(new GetManualOperatorsReportQuery(From, To), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static TripFactsEntry Trip(
        string finalStatus, string? acknowledgedBy, bool isManual = true, int? timeToCompleteSec = null)
        => new(
            Guid.NewGuid(), null, null, null,
            null, null,
            finalStatus, null, 0,
            From.AddHours(1), null, null, null,
            null, null, null, null,
            null, timeToCompleteSec, null,
            From.AddHours(1),
            IsManual: isManual,
            AcknowledgedBy: acknowledgedBy);
}
