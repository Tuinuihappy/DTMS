using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Planning.Infrastructure.Repositories;

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
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<Job?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        // Partial index on Jobs.TripId (DbContext line 58) makes this an
        // index probe. Returns null when no Job has been MarkDispatched
        // against this trip yet (or for legacy pre-b8 trips).
        return await _context.Jobs
            .Include(j => j.Legs)
            .FirstOrDefaultAsync(j => j.TripId == tripId, cancellationToken);
    }

    public async Task<List<Job>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.DeliveryOrderId == deliveryOrderId)
            .Include(j => j.Legs)
            .OrderBy(j => j.GroupIndex)
            .ThenBy(j => j.CreatedAt)
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

    public async Task<(List<Job> Items, int TotalCount)> SearchQueueAsync(
        IReadOnlyList<JobStatus> statuses,
        int page,
        int pageSize,
        string? sortBy = null,
        bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Jobs.AsQueryable();
        if (statuses.Count > 0)
            query = query.Where(j => statuses.Contains(j.Status));

        var total = await query.CountAsync(cancellationToken);
        var ordered = ApplyQueueOrdering(query, sortBy, sortDescending);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(j => j.Legs)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // Maps the operator-facing column tokens to LINQ ordering. Unknown
    // values fall back to CreatedAt desc — the original hardcoded order
    // that "newest failures at the top" is built on, so clients that
    // forget to send sortBy keep seeing the same queue.
    private static IOrderedQueryable<Job> ApplyQueueOrdering(
        IQueryable<Job> query, string? sortBy, bool descending)
    {
        return (sortBy?.ToLowerInvariant(), descending) switch
        {
            ("attemptnumber", false) => query
                .OrderBy(j => j.AttemptNumber)
                .ThenByDescending(j => j.CreatedAt),
            ("attemptnumber", true) => query
                .OrderByDescending(j => j.AttemptNumber)
                .ThenByDescending(j => j.CreatedAt),
            ("status", false) => query
                .OrderBy(j => j.Status)
                .ThenByDescending(j => j.CreatedAt),
            ("status", true) => query
                .OrderByDescending(j => j.Status)
                .ThenByDescending(j => j.CreatedAt),
            ("sladeadline", false) => query
                // Nulls last on asc so jobs without an SLA don't crowd
                // out the imminent ones at the top of the queue.
                .OrderBy(j => j.SlaDeadline == null)
                .ThenBy(j => j.SlaDeadline),
            ("sladeadline", true) => query
                .OrderBy(j => j.SlaDeadline == null)
                .ThenByDescending(j => j.SlaDeadline),
            ("createdat", false) => query.OrderBy(j => j.CreatedAt),
            _ => query.OrderByDescending(j => j.CreatedAt),
        };
    }
}
