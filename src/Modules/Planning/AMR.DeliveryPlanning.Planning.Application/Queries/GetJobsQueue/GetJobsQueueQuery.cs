using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobsQueue;

/// <summary>
/// Phase b10-frontend.2 — paginated operator queue across every order.
/// Filters by an arbitrary status set so the UI can drive "Failed",
/// "Stuck" (Created/Assigned/Committed), and "All" tabs from one query.
/// Empty Statuses = no filter. Default order is newest-first by
/// CreatedAt so freshly failed jobs surface at the top; passing
/// <paramref name="SortBy"/> swaps to attemptNumber/status/slaDeadline.
/// </summary>
public record GetJobsQueueQuery(
    IReadOnlyList<JobStatus> Statuses,
    int Page,
    int PageSize,
    string? SortBy = null,
    bool SortDescending = true
) : IQuery<JobsQueueResult>;

public record JobsQueueResult(
    IReadOnlyList<JobDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
