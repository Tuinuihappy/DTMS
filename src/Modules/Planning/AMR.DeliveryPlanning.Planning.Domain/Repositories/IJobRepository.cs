using AMR.DeliveryPlanning.Planning.Domain.Entities;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Job>> GetPendingJobsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
}
