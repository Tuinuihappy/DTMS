using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Infrastructure.Database;

/// <summary>
/// Hosted service that keeps Postgres range-partitioned tables fed with
/// the next few months of partitions and drops any whose retention has
/// expired. Generic over the EF Core <typeparamref name="TContext"/> so
/// each owning module registers its own instance with its own targets;
/// the composition root wires both.
/// </summary>
/// <remarks>
/// <para>Convention: partition names are <c>{table}_{YYYYMM}</c>. The
/// drop side filters strictly on that suffix so a partition with a
/// different naming scheme (or the parent table itself) is never
/// touched.</para>
/// <para>If this service is down for an extended period, the migration
/// that creates the partitioned table is expected to seed enough buffer
/// (current month + 2 next months) to survive several days of outage.
/// Production checklist alerts on missing next-month partition.</para>
/// </remarks>
public sealed class PartitionMaintenanceService<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _dbFactory;
    private readonly PartitionMaintenanceOptions<TContext> _opts;
    private readonly ILogger<PartitionMaintenanceService<TContext>> _log;

    public PartitionMaintenanceService(
        IDbContextFactory<TContext> dbFactory,
        IOptions<PartitionMaintenanceOptions<TContext>> opts,
        ILogger<PartitionMaintenanceService<TContext>> log)
    {
        _dbFactory = dbFactory;
        _opts = opts.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_opts.Targets.Count == 0)
        {
            _log.LogInformation(
                "Partition maintenance for {Context}: no targets configured, idle.",
                typeof(TContext).Name);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var target in _opts.Targets)
                {
                    ValidateIdentifier(target.SchemaAndTable);
                    await CreateUpcomingPartitionsAsync(target, stoppingToken);
                    await DropExpiredPartitionsAsync(target, stoppingToken);
                }
                _log.LogDebug(
                    "Partition maintenance cycle ok for {Context} ({Count} targets).",
                    typeof(TContext).Name, _opts.Targets.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Partition maintenance cycle failed for {Context}", typeof(TContext).Name);
            }

            try
            {
                await Task.Delay(_opts.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CreateUpcomingPartitionsAsync(PartitionTarget t, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (schema, bareTable) = SplitIdentifier(t.SchemaAndTable);
        // Snapshot DateTime.UtcNow once — reading Year and Month
        // separately would otherwise race the month-boundary tick
        // (canonical "monthly cron broken on Dec 31" footgun).
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i <= t.AdvanceMonths; i++)
        {
            var from = thisMonth.AddMonths(i);
            var to = thisMonth.AddMonths(i + 1);
            var partName = $"{bareTable}_{from:yyyyMM}";
            // Postgres-quote both segments so PascalCase table names
            // ("SystemRequestLog") aren't silently lowercased by the
            // parser. Schema + bareTable have already passed the
            // identifier validator so the only chars inside the quotes
            // are [A-Za-z0-9_].
            var qualifiedPart = $"\"{schema}\".\"{partName}\"";
            var qualifiedParent = $"\"{schema}\".\"{bareTable}\"";
            var sql = $@"
                CREATE TABLE IF NOT EXISTS {qualifiedPart} PARTITION OF {qualifiedParent}
                FOR VALUES FROM ('{from:yyyy-MM-dd}') TO ('{to:yyyy-MM-dd}');";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
    }

    private async Task DropExpiredPartitionsAsync(PartitionTarget t, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (schema, bareTable) = SplitIdentifier(t.SchemaAndTable);
        var cutoff = DateTime.UtcNow.AddMonths(-t.RetainMonths);
        var cutoffSuffix = cutoff.ToString("yyyyMM");

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var names = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT inh.relname
                FROM pg_inherits i
                JOIN pg_class parent ON i.inhparent = parent.oid
                JOIN pg_class inh    ON i.inhrelid  = inh.oid
                JOIN pg_namespace parent_ns ON parent.relnamespace = parent_ns.oid
                WHERE parent.relname = @table
                  AND parent_ns.nspname = @schema
                  AND inh.relname ~ ('^' || @prefix || '[0-9]{6}$')
                  AND right(inh.relname, 6) < @cutoff;";
            AddParam(cmd, "table", bareTable);
            AddParam(cmd, "schema", schema);
            AddParam(cmd, "prefix", $"{bareTable}_");
            AddParam(cmd, "cutoff", cutoffSuffix);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                names.Add(reader.GetString(0));
        }

        foreach (var p in names)
        {
            ValidateBareIdentifier(p);
            var dropSql = $"DROP TABLE IF EXISTS \"{schema}\".\"{p}\";";
            await db.Database.ExecuteSqlRawAsync(dropSql, ct);
            _log.LogInformation(
                "Dropped expired partition {Schema}.{Partition} (cutoff {Cutoff})",
                schema, p, cutoffSuffix);
        }
    }

    private static (string Schema, string Table) SplitIdentifier(string id)
    {
        int dot = id.IndexOf('.');
        return dot >= 0
            ? (id[..dot], id[(dot + 1)..])
            : ("public", id);
    }

    private static void ValidateIdentifier(string identifier)
    {
        // We interpolate this into DDL — caller-controlled config, but
        // defensive guard makes future contributors safer.
        foreach (char c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                throw new ArgumentException(
                    $"Invalid identifier '{identifier}': only [A-Za-z0-9_.] allowed.");
        }
    }

    private static void ValidateBareIdentifier(string identifier)
    {
        foreach (char c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new InvalidOperationException(
                    $"Refusing to drop partition with suspicious name '{identifier}'.");
        }
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

public sealed class PartitionMaintenanceOptions<TContext> where TContext : DbContext
{
    public IReadOnlyList<PartitionTarget> Targets { get; set; } = Array.Empty<PartitionTarget>();
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}

/// <param name="SchemaAndTable">Fully qualified table name, e.g. <c>iam.system_request_log</c>.</param>
/// <param name="TimeColumn">Name of the partition key column — informational; not used in SQL.</param>
/// <param name="AdvanceMonths">Number of future months to pre-create (in addition to current).</param>
/// <param name="RetainMonths">Drop partitions older than this many months from now.</param>
public sealed record PartitionTarget(
    string SchemaAndTable,
    string TimeColumn,
    int AdvanceMonths,
    int RetainMonths);
