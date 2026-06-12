using AMR.DeliveryPlanning.Planning.Domain.Entities;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    // Phase b10 — all Jobs for an order (one per station-pair group).
    // Order matters: sorted by GroupIndex asc so the operator UI lines
    // them up the same way as the consumer's dispatch loop.
    Task<List<Job>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default);
    Task<List<Job>> GetPendingJobsAsync(CancellationToken cancellationToken = default);
    Task<List<Job>> GetAtRiskJobsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
    Task AddDependencyAsync(JobDependency dependency, CancellationToken cancellationToken = default);
    Task AddMilkRunTemplateAsync(MilkRunTemplate template, CancellationToken cancellationToken = default);
    Task<List<MilkRunTemplate>> GetActiveMilkRunTemplatesAsync(CancellationToken cancellationToken = default);
}
