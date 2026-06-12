using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobsQueue;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Planning.UnitTests;

// Phase b10-frontend.2 — the queue handler is thin (validates input,
// delegates to repo, maps to DTO). These tests pin down the validation
// contract + mapping shape so the operator UI doesn't regress.
public class GetJobsQueueQueryHandlerTests
{
    [Fact]
    public async Task RejectsPageLessThanOne()
    {
        var (handler, _) = HandlerWith(new List<Job>(), totalCount: 0);

        var result = await handler.Handle(
            new GetJobsQueueQuery(new List<JobStatus>(), Page: 0, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    public async Task RejectsInvalidPageSize(int pageSize)
    {
        var (handler, _) = HandlerWith(new List<Job>(), totalCount: 0);

        var result = await handler.Handle(
            new GetJobsQueueQuery(new List<JobStatus>(), Page: 1, PageSize: pageSize),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PageSize");
    }

    [Fact]
    public async Task HappyPath_MapsItemsAndPropagatesPagination()
    {
        var jobs = new List<Job>
        {
            new Job(Guid.NewGuid(), "Normal"),
            new Job(Guid.NewGuid(), "Normal"),
        };
        var (handler, repo) = HandlerWith(jobs, totalCount: 42);

        var statuses = new List<JobStatus> { JobStatus.Failed, JobStatus.Created };
        var result = await handler.Handle(
            new GetJobsQueueQuery(statuses, Page: 3, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(42);
        result.Value.Page.Should().Be(3);
        result.Value.PageSize.Should().Be(20);

        await repo.Received(1).SearchQueueAsync(
            Arg.Is<IReadOnlyList<JobStatus>>(s => s.SequenceEqual(statuses)),
            3, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyStatusSet_PassesEmptyListThrough()
    {
        var (handler, repo) = HandlerWith(new List<Job>(), totalCount: 0);

        var result = await handler.Handle(
            new GetJobsQueueQuery(new List<JobStatus>(), Page: 1, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await repo.Received(1).SearchQueueAsync(
            Arg.Is<IReadOnlyList<JobStatus>>(s => s.Count == 0),
            1, 20, Arg.Any<CancellationToken>());
    }

    private static (GetJobsQueueQueryHandler handler, IJobRepository repo)
        HandlerWith(List<Job> items, int totalCount)
    {
        var repo = Substitute.For<IJobRepository>();
        repo.SearchQueueAsync(
                Arg.Any<IReadOnlyList<JobStatus>>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((items, totalCount));
        return (new GetJobsQueueQueryHandler(repo), repo);
    }
}
