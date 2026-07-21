using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DTMS.Planning.Infrastructure.Repositories;

public class DispatchClaimRepository : IDispatchClaimRepository
{
    // 23505 = unique_violation. Someone else claimed the key first.
    private const string UniqueViolation = "23505";

    private readonly PlanningDbContext _context;

    public DispatchClaimRepository(PlanningDbContext context)
    {
        _context = context;
    }

    public async Task<DispatchClaim?> TryClaimAsync(
        string idempotencyKey,
        Guid orderTemplateId,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var claim = new DispatchClaim(idempotencyKey, orderTemplateId, requestHash);
        _context.DispatchClaims.Add(claim);
        try
        {
            // A single SaveChanges is its own implicit transaction, so this
            // needs no CreateExecutionStrategy wrapper (that is only required
            // around explicit BeginTransactionAsync under EnableRetryOnFailure).
            await _context.SaveChangesAsync(cancellationToken);
            return claim;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Lost the race — detach so the failed insert doesn't poison later
            // SaveChanges on this same DbContext instance.
            _context.Entry(claim).State = EntityState.Detached;
            return null;
        }
    }

    public Task<DispatchClaim?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var normalized = (idempotencyKey ?? string.Empty).Trim();
        return _context.DispatchClaims
            .FirstOrDefaultAsync(c => c.IdempotencyKey == normalized, cancellationToken);
    }

    public Task<DispatchClaim?> GetLatestByTemplateAsync(Guid orderTemplateId, CancellationToken cancellationToken = default)
        => _context.DispatchClaims
            .AsNoTracking()
            .Where(c => c.OrderTemplateId == orderTemplateId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
