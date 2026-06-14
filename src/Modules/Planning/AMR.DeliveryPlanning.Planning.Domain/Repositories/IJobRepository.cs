using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

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
    Task<List<Job>> GetAtRiskJobsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);
    /// <summary>
    /// Phase b10-frontend.2 queue page — paginated job listing across all
    /// orders. Filters by a status set (empty = all statuses). Sorted
    /// newest-first by CreatedAt so the operator sees fresh failures at
    /// the top. Returns (page items, total matching count) tuple.
    /// </summary>
    Task<(List<Job> Items, int TotalCount)> SearchQueueAsync(
        IReadOnlyList<JobStatus> statuses,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
    Task AddDependencyAsync(JobDependency dependency, CancellationToken cancellationToken = default);
    Task AddMilkRunTemplateAsync(MilkRunTemplate template, CancellationToken cancellationToken = default);
    Task<List<MilkRunTemplate>> GetActiveMilkRunTemplatesAsync(CancellationToken cancellationToken = default);
}
