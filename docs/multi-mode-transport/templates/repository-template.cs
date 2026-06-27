// =============================================================================
// REPOSITORY TEMPLATE
// =============================================================================
//
// Repository = persistence boundary for an aggregate. Lives in Infrastructure
// (implementation) with interface in Domain.
//
// Filename layout:
//   src/Modules/{Module}/DTMS.{Module}.Domain/Repositories/I{Entity}Repository.cs
//   src/Modules/{Module}/DTMS.{Module}.Infrastructure/Repositories/{Entity}Repository.cs
//
// Reference examples (read these first):
//   src/Modules/Dispatch/.../Infrastructure/Repositories/TripRepository.cs
//     ↑ canonical: Include for child collections, IgnoreQueryFilters for
//       system contexts (webhooks, reconciliation), recursive CTE example
//   src/Modules/DeliveryOrder/.../Infrastructure/Repositories/DeliveryOrderRepository.cs
//     ↑ aggregate with retry execution strategy wrapper
//   src/Modules/Planning/.../Infrastructure/Repositories/  (various)
//
// Critical conventions:
//   1. Interface in Domain, impl in Infrastructure — Domain knows NO EF Core
//   2. Inject DbContext directly (NOT DbSet, NOT IDbContextFactory unless special)
//   3. Always accept CancellationToken (last param, default value OK)
//   4. Use Include() for child collections needed at load time
//   5. Use AsNoTracking() ONLY for read-only queries (projections, list views)
//   6. Use IgnoreQueryFilters() when:
//        - Vendor webhooks (no tenant claim)
//        - System background services (PlanningReconciliation, etc.)
//        - Recursive queries (chain traversal)
//   7. Per memory [project_npgsql_retry_execution_strategy]:
//        - DbContexts have EnableRetryOnFailure
//        - Explicit BeginTransactionAsync MUST wrap in CreateExecutionStrategy().ExecuteAsync
//        - Or EF throws "An exception has been raised that is likely due to a transient failure"
//   8. Return null for "not found" — repository is not the right layer to
//      decide if "not found" is an error (caller does that)
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

// =============================================================================
// FILE 1: I{Entity}Repository.cs  (in Domain/Repositories/)
// =============================================================================

using DTMS.{Module}.Domain.Entities;

namespace DTMS.{Module}.Domain.Repositories;

/// <summary>
/// Persistence boundary for <see cref="{Entity}"/>.
/// Implementation in Infrastructure layer.
/// </summary>
public interface I{Entity}Repository
{
    // ─── Single-entity queries ────────────────────────────────────────────

    Task<{Entity}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<{Entity}?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    // ─── Collection queries ───────────────────────────────────────────────

    Task<List<{Entity}>> GetActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    Task<List<{Entity}>> GetStaleAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);

    // ─── Existence checks (cheaper than fetching) ─────────────────────────

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    // ─── Mutations ────────────────────────────────────────────────────────

    Task AddAsync({Entity} entity, CancellationToken cancellationToken = default);

    Task UpdateAsync({Entity} entity, CancellationToken cancellationToken = default);

    // Soft delete preferred — hard delete only when you mean it
    Task DeleteAsync({Entity} entity, CancellationToken cancellationToken = default);

    // ─── Unit of Work ─────────────────────────────────────────────────────
    // (Choose ONE pattern per module — don't mix)

    /// <summary>Persists all tracked changes in this scope.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}


// =============================================================================
// FILE 2: {Entity}Repository.cs  (in Infrastructure/Repositories/)
// =============================================================================

using DTMS.{Module}.Domain.Entities;
using DTMS.{Module}.Domain.Enums;
using DTMS.{Module}.Domain.Repositories;
using DTMS.{Module}.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.{Module}.Infrastructure.Repositories;

public class {Entity}Repository : I{Entity}Repository
{
    private readonly {Module}DbContext _context;

    public {Entity}Repository({Module}DbContext context)
    {
        _context = context;
    }

    // ─── Single-entity queries ────────────────────────────────────────────

    public async Task<{Entity}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.{DbSet}
            .Include(e => e.{ChildCollection})         // eagerly load needed children
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<{Entity}?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // IgnoreQueryFilters: e.g. system webhooks resolve by external code
        // without tenant context. Use ONLY when justified — explain why in comment.
        return await _context.{DbSet}
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Code == code, cancellationToken);
    }

    // ─── Collection queries (read-only — use AsNoTracking) ────────────────

    public async Task<List<{Entity}>> GetActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        return await _context.{DbSet}
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.Status == {Entity}Status.Active)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<{Entity}>> GetStaleAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: reconciliation runs as system service.
        return await _context.{DbSet}
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.Status == {Entity}Status.InProgress
                     && e.UpdatedAt < cutoffUtc)
            .ToListAsync(cancellationToken);
    }

    // ─── Existence check (faster than fetching) ───────────────────────────

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.{DbSet}
            .AsNoTracking()
            .AnyAsync(e => e.Id == id, cancellationToken);
    }

    // ─── Mutations ────────────────────────────────────────────────────────

    public async Task AddAsync({Entity} entity, CancellationToken cancellationToken = default)
    {
        await _context.{DbSet}.AddAsync(entity, cancellationToken);
        // Note: SaveChangesAsync is caller's responsibility — supports
        // multi-aggregate transactions via outer ExecuteAsync
    }

    public Task UpdateAsync({Entity} entity, CancellationToken cancellationToken = default)
    {
        // EF tracks via Include — re-attaching is needed only if entity was
        // loaded AsNoTracking. Prefer NOT to use AsNoTracking when caller will mutate.
        _context.{DbSet}.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync({Entity} entity, CancellationToken cancellationToken = default)
    {
        _context.{DbSet}.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ─── Advanced query patterns ──────────────────────────────────────────

    /// <summary>
    /// Recursive query example — chain traversal via CTE.
    /// Pattern from TripRepository.GetRootTripIdAsync.
    /// Use raw SQL when LINQ can't express it cleanly.
    /// </summary>
    public async Task<Guid> GetRootChainIdAsync(Guid leafId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: vendor webhooks fire without tenant context.
        var rootIds = await _context.Database
            .SqlQuery<Guid>($@"
                WITH RECURSIVE chain AS (
                    SELECT ""Id"", ""PreviousId""
                    FROM {schema}.""{Table}""
                    WHERE ""Id"" = {leafId}
                    UNION ALL
                    SELECT t.""Id"", t.""PreviousId""
                    FROM {schema}.""{Table}"" t
                    INNER JOIN chain c ON t.""Id"" = c.""PreviousId""
                )
                SELECT ""Id"" FROM chain WHERE ""PreviousId"" IS NULL")
            .ToListAsync(cancellationToken);

        return rootIds.FirstOrDefault();
    }
}


// =============================================================================
// TRANSACTION PATTERN (when explicit transaction needed)
// =============================================================================
//
// Per memory [project_npgsql_retry_execution_strategy]:
// DbContexts use EnableRetryOnFailure. Explicit BeginTransactionAsync MUST
// wrap in CreateExecutionStrategy().ExecuteAsync — otherwise EF throws.
//
// Usage (in a command handler — NOT inside repository):
//
//   var strategy = _context.Database.CreateExecutionStrategy();
//   await strategy.ExecuteAsync(async () =>
//   {
//       await using var tx = await _context.Database.BeginTransactionAsync(ct);
//       try
//       {
//           // ... multiple repository calls
//           await _repo.AddAsync(entity1, ct);
//           await _repo.UpdateAsync(entity2, ct);
//           await _repo.SaveChangesAsync(ct);
//           await tx.CommitAsync(ct);
//       }
//       catch
//       {
//           await tx.RollbackAsync(ct);
//           throw;
//       }
//   });


// =============================================================================
// REGISTRATION (in module ServiceCollectionExtensions)
// =============================================================================
//
// services.AddScoped<I{Entity}Repository, {Entity}Repository>();


// =============================================================================
// TESTING (in tests/Modules/{Module}.UnitTests/Fakes/)
// =============================================================================
//
// In-memory fake for handler unit tests:
//
//   public sealed class Fake{Entity}Repository : I{Entity}Repository
//   {
//       private readonly Dictionary<Guid, {Entity}> _store = new();
//
//       public Task<{Entity}?> GetByIdAsync(Guid id, CancellationToken ct = default)
//           => Task.FromResult(_store.TryGetValue(id, out var e) ? e : null);
//
//       public Task AddAsync({Entity} entity, CancellationToken ct = default)
//       {
//           _store[entity.Id] = entity;
//           return Task.CompletedTask;
//       }
//
//       public Task UpdateAsync({Entity} entity, CancellationToken ct = default)
//       {
//           _store[entity.Id] = entity;
//           return Task.CompletedTask;
//       }
//
//       public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
//
//       // Test helpers
//       public void Seed({Entity} entity) => _store[entity.Id] = entity;
//       public IReadOnlyCollection<{Entity}> All => _store.Values;
//   }
//
// For full DB roundtrip use integration tests (Testcontainers).
