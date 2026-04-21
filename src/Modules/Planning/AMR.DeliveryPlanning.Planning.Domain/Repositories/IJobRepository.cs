using AMR.DeliveryPlanning.Planning.Domain.Entities;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Job>> GetPendingJobsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
    Task AddDependencyAsync(JobDependency dependency, CancellationToken cancellationToken = default);
    Task AddMilkRunTemplateAsync(MilkRunTemplate template, CancellationToken cancellationToken = default);
    Task<List<MilkRunTemplate>> GetActiveMilkRunTemplatesAsync(CancellationToken cancellationToken = default);
}
