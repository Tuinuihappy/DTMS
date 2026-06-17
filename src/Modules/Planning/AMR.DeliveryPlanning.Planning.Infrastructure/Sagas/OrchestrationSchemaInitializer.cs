using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Sagas;

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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        var created = await db.Database.EnsureCreatedAsync(cancellationToken);
        if (created)
            _logger.LogInformation(
                "[OrchestrationSchema] created orchestration schema (POC bootstrap) — table {Table}",
                nameof(OrchestrationDbContext.DeliveryOrderSagas));
        else
            _logger.LogDebug(
                "[OrchestrationSchema] orchestration schema already present — no-op");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
