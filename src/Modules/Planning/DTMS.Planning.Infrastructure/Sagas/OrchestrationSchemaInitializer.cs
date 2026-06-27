using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Infrastructure.Sagas;

/// <summary>
/// T2 POC — minimal schema bootstrap. When the saga feature flag is on,
/// materialize <c>orchestration.DeliveryOrderSagas</c> at startup so the
/// saga repository has somewhere to write. Skips silently if the table
/// already exists.
///
/// <para><b>This is POC scaffolding</b>. A proper EF migration follows in
/// Phase 2 once the saga's schema is stable — at that point this initializer
/// is removed and the table joins the normal migration pipeline (per the
/// "EF migrations must be created manually" convention noted in repo
/// memory).</para>
/// </summary>
public sealed class OrchestrationSchemaInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrchestrationSchemaInitializer> _logger;

    public OrchestrationSchemaInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<OrchestrationSchemaInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // EnsureCreated only creates the database, not schemas-or-tables in an
        // existing one. Our database (amr_delivery_planning) already exists
        // with planning/dispatch/etc. schemas, so EnsureCreated is a no-op
        // here. Drop to idempotent raw SQL — CREATE … IF NOT EXISTS — so a
        // restart with the flag still on is a fast no-op. Phase 2 replaces
        // this with a proper EF migration in the saga's own migrations
        // assembly.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE SCHEMA IF NOT EXISTS orchestration;

            CREATE TABLE IF NOT EXISTS orchestration.""DeliveryOrderSagas"" (
                ""CorrelationId"" uuid NOT NULL PRIMARY KEY,
                ""CurrentState"" integer NOT NULL,
                ""JobId"" uuid NULL,
                ""TripId"" uuid NULL,
                ""VendorMissionId"" character varying(200) NULL,
                ""LastFaultMessage"" character varying(2000) NULL,
                ""RetryCount"" integer NOT NULL DEFAULT 0,
                ""Version"" integer NOT NULL DEFAULT 0,
                ""UpdatedAtUtc"" timestamp with time zone NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ""IX_DeliveryOrderSagas_CurrentState""
                ON orchestration.""DeliveryOrderSagas"" (""CurrentState"");
            CREATE INDEX IF NOT EXISTS ""IX_DeliveryOrderSagas_UpdatedAtUtc""
                ON orchestration.""DeliveryOrderSagas"" (""UpdatedAtUtc"");
        ", cancellationToken);

        _logger.LogInformation(
            "[OrchestrationSchema] ensured orchestration.DeliveryOrderSagas (POC bootstrap)");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
