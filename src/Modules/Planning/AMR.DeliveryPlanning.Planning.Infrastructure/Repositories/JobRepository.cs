using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Repositories;

public class JobRepository : IJobRepository
{
    private readonly PlanningDbContext _context;

    public JobRepository(PlanningDbContext context)
    {
        _context = context;
    }

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Include(j => j.Legs)
                .ThenInclude(l => l.Stops)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<List<Job>> GetPendingJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.Status == JobStatus.Created)
            .Include(j => j.Legs)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        await _context.Jobs.AddAsync(job, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Job job, CancellationToken cancellationToken = default)
    {
        _context.Jobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddDependencyAsync(JobDependency dependency, CancellationToken cancellationToken = default)
    {
        await _context.JobDependencies.AddAsync(dependency, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMilkRunTemplateAsync(MilkRunTemplate template, CancellationToken cancellationToken = default)
    {
        await _context.MilkRunTemplates.AddAsync(template, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<MilkRunTemplate>> GetActiveMilkRunTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MilkRunTemplates
            .Where(t => t.IsActive)
            .Include(t => t.Stops)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Job>> GetAtRiskJobsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => (j.Status == JobStatus.Assigned || j.Status == JobStatus.Committed)
                     && j.SlaDeadline.HasValue
                     && j.SlaDeadline.Value <= cutoffTime)
            .Include(j => j.Legs)
            .ToListAsync(cancellationToken);
    }
}
