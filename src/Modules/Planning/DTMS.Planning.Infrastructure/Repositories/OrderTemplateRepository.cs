using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DTMS.Planning.Infrastructure.Repositories;

public class OrderTemplateRepository : IOrderTemplateRepository
{
    private readonly PlanningDbContext _context;

    public OrderTemplateRepository(PlanningDbContext context)
    {
        _context = context;
    }

    public Task<OrderTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.OrderTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<OrderTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        return _context.OrderTemplates
            .FirstOrDefaultAsync(t => t.Name.ToLower() == normalized.ToLower(), cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        var query = _context.OrderTemplates.Where(t => t.Name.ToLower() == normalized.ToLower());
        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<OrderTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        string? search = null,
        bool? isActive = null,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<OrderTemplate> query;
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Missions is a jsonb document behind a value converter, so LINQ
            // can't reach into it — the mission actionTemplateName branch has
            // to be raw SQL. FromSql keeps it composable: the isActive filter,
            // ordering and paging below wrap this as a subquery.
            //
            // FromSqlRaw with an explicitly named parameter, NOT
            // FromSqlInterpolated: interpolation names its parameters p0, p1,
            // … and the paging EF composes on top names LIMIT/OFFSET @p0/@p00
            // — the collision hands LIMIT the search text (42804). A named
            // @needle lives outside that namespace, and one parameter can be
            // referenced from all five branches.
            var needle = new NpgsqlParameter("needle", $"%{search.Trim()}%");
            query = _context.OrderTemplates.FromSqlRaw("""
                SELECT * FROM planning."OrderTemplates" t
                WHERE t."Name" ILIKE @needle
                   OR t."Description" ILIKE @needle
                   OR t."AppointVehicleName" ILIKE @needle
                   OR t."AppointVehicleGroupName" ILIKE @needle
                   OR EXISTS (
                        SELECT 1 FROM jsonb_array_elements(t."Missions") m
                        WHERE m->>'actionTemplateName' ILIKE @needle)
                """, needle);
        }
        else
        {
            query = _context.OrderTemplates.AsQueryable();
        }

        // Explicit isActive beats the legacy includeInactive flag — the UI's
        // tri-state filter (All/Active/Inactive) sends isActive for the two
        // narrow states and includeInactive=true for All.
        if (isActive.HasValue)
            query = query.Where(t => t.IsActive == isActive.Value);
        else if (!includeInactive)
            query = query.Where(t => t.IsActive);

        // LongCount keeps the API safe past 2B rows; running it before the
        // page slice means total + page slice come from the same snapshot
        // even if a concurrent insert lands between the two SQL round trips.
        var total = await query.LongCountAsync(cancellationToken);
        var ordered = ApplyOrdering(query, sortBy, sortDescending);
        var items = await ordered
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // Maps the frontend sort-column tokens to LINQ ordering. Unknown
    // values fall back to Name asc so a forgetful client sees the same
    // deterministic order the catalog had before sortBy was introduced.
    private static IOrderedQueryable<OrderTemplate> ApplyOrdering(
        IQueryable<OrderTemplate> query, string? sortBy, bool descending)
    {
        return sortBy switch
        {
            "priority" => descending
                ? query.OrderByDescending(t => t.Priority).ThenBy(t => t.Name)
                : query.OrderBy(t => t.Priority).ThenBy(t => t.Name),
            "isActive" => descending
                ? query.OrderByDescending(t => t.IsActive).ThenBy(t => t.Name)
                : query.OrderBy(t => t.IsActive).ThenBy(t => t.Name),
            "modifiedAt" => descending
                // ModifiedAt is null on never-edited rows; fall back to
                // CreatedAt so the column reads as "last touched" rather
                // than bucketing every fresh row to the bottom.
                ? query.OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt)
                : query.OrderBy(t => t.ModifiedAt ?? t.CreatedAt),
            "createdAt" => descending
                ? query.OrderByDescending(t => t.CreatedAt)
                : query.OrderBy(t => t.CreatedAt),
            _ => descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
        };
    }

    public async Task<(int Total, int Active, double AvgMissions, int WithVehicleBinding)> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        // Single round trip. AVG(jsonb_array_length) forces raw SQL — the
        // Missions column is opaque to LINQ (value-converted jsonb) — so the
        // rest of the counters ride along in the same statement.
        var row = await _context.Database
            .SqlQuery<OrderTemplateStatsRow>($"""
                SELECT
                    COUNT(*)::int AS "Total",
                    COALESCE(COUNT(*) FILTER (WHERE "IsActive"), 0)::int AS "Active",
                    COALESCE(AVG(jsonb_array_length("Missions")), 0)::float8 AS "AvgMissions",
                    COALESCE(COUNT(*) FILTER (WHERE "AppointVehicleKey" IS NOT NULL
                        OR "AppointVehicleName" IS NOT NULL
                        OR "AppointVehicleGroupKey" IS NOT NULL
                        OR "AppointVehicleGroupName" IS NOT NULL
                        OR "AppointQueueWaitArea" IS NOT NULL), 0)::int AS "WithVehicleBinding"
                FROM planning."OrderTemplates"
                """)
            .SingleAsync(cancellationToken);
        return (row.Total, row.Active, row.AvgMissions, row.WithVehicleBinding);
    }

    private sealed class OrderTemplateStatsRow
    {
        public int Total { get; set; }
        public int Active { get; set; }
        public double AvgMissions { get; set; }
        public int WithVehicleBinding { get; set; }
    }

    public Task<OrderTemplate?> FindByRouteAsync(
        Guid pickupStationId,
        Guid dropStationId,
        CancellationToken cancellationToken = default)
    {
        return _context.OrderTemplates
            .Where(t => t.IsActive
                     && t.PickupStationId == pickupStationId
                     && t.DropStationId == dropStationId)
            .OrderBy(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task AddAsync(OrderTemplate template, CancellationToken cancellationToken = default)
        => _context.OrderTemplates.AddAsync(template, cancellationToken).AsTask();

    public void Update(OrderTemplate template) => _context.OrderTemplates.Update(template);

    public void Remove(OrderTemplate template) => _context.OrderTemplates.Remove(template);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
