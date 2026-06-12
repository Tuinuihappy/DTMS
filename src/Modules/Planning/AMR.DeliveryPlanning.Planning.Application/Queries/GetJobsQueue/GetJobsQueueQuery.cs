using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobsQueue;

/// <summary>
/// Phase b10-frontend.2 — paginated operator queue across every order.
/// Filters by an arbitrary status set so the UI can drive "Failed",
/// "Stuck" (Created/Assigned/Committed), and "All" tabs from one query.
/// Empty Statuses = no filter. Items sorted newest-first so freshly
/// failed jobs surface at the top of the queue.
/// </summary>
public record GetJobsQueueQuery(
    IReadOnlyList<JobStatus> Statuses,
    int Page,
    int PageSize
) : IQuery<JobsQueueResult>;

public record JobsQueueResult(
    IReadOnlyList<JobDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
