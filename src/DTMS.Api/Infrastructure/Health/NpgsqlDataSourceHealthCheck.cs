using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace DTMS.Api.Infrastructure.Health;

// Phase B Step B2 — postgres readiness probe that REUSES the singleton
// NpgsqlDataSource (registered in ModuleServiceRegistration.AddAllModules)
// instead of opening a fresh raw NpgsqlConnection per call.
//
// Before this, every /health/ready hit constructed a new
// `new NpgsqlConnection(connectionString)` and opened it. That went
// through Npgsql's static pool keyed by connection string — a SECOND
// pool, separate from the one all 9 DbContexts share. With Docker /
// k8s liveness probes hitting /health/ready every few seconds, that
// produced a steady churn of independent connection objects + pool
// pressure that didn't show up under DbContext metrics.
//
// After: every probe pulls from the same shared pool — connection
// counts at idle stay flat regardless of probe frequency.
internal sealed class NpgsqlDataSourceHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlDataSourceHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
