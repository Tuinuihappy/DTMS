using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;

namespace DTMS.Planning.Domain.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    // Phase #1 — reverse lookup for the Trip→Job pause/resume mirror.
    // Trip pause webhooks only carry TripId (no JobId), so the
    // consumer needs this to find the linked Job. Returns null when
    // no Job has TripId set to this id (legacy pre-b8 trips or trips
    // that never reached MarkDispatched).
    Task<Job?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default);
    // Phase b10 — all Jobs for an order (one per station-pair group).
    // Order matters: sorted by GroupIndex asc so the operator UI lines
    // them up the same way as the consumer's dispatch loop.
    Task<List<Job>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Phase b10-frontend.2 queue page — paginated job listing across all
    /// orders. Filters by a status set (empty = all statuses). Default
    /// order is newest-first by CreatedAt so the operator sees fresh
    /// failures at the top; passing <paramref name="sortBy"/> swaps to
    /// attemptNumber/status/slaDeadline. Returns (page items, total
    /// matching count) tuple.
    /// </summary>
    Task<(List<Job> Items, int TotalCount)> SearchQueueAsync(
        IReadOnlyList<JobStatus> statuses,
        int page,
        int pageSize,
        string? sortBy = null,
        bool sortDescending = true,
        CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
    // NOTE: AddDependencyAsync / milk-run template methods / GetAtRiskJobsAsync
    // were removed 2026-07-17 with the legacy manual-planning stack — their
    // only callers were the deleted cross-dock / milk-run / SLA-replan flows.
}
