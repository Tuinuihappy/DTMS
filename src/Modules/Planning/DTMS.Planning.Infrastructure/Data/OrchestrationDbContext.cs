using DTMS.Planning.Infrastructure.Sagas;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Planning.Infrastructure.Data;

/// <summary>
/// T2 — dedicated DbContext for saga persistence. Lives in the
/// <c>orchestration</c> schema so saga migrations are decoupled from
/// Planning's domain migrations — flipping the saga feature flag must
/// never touch tables that production Planning queries reflect on.
///
/// <para>Only carries the <see cref="DeliveryOrderSagaInstance"/> table for
/// now; further saga types (e.g. one per long-running workflow) join here
/// rather than spawning a new DbContext each.</para>
/// </summary>
public class OrchestrationDbContext : DbContext
{
    public const string Schema = "orchestration";

    public DbSet<DeliveryOrderSagaInstance> DeliveryOrderSagas => Set<DeliveryOrderSagaInstance>();

    public OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DeliveryOrderSagaInstance>(b =>
        {
            b.ToTable("DeliveryOrderSagas");
            b.HasKey(x => x.CorrelationId);

            // MassTransit's optimistic-concurrency strategy: this column is
            // bumped by the framework on every saga write and used in the
            // UPDATE's WHERE clause.
            b.Property(x => x.Version).IsConcurrencyToken();

            b.Property(x => x.CurrentState).IsRequired();
            b.Property(x => x.LastFaultMessage).HasMaxLength(2000);
            b.Property(x => x.VendorMissionId).HasMaxLength(200);
            b.Property(x => x.UpdatedAtUtc).IsRequired();

            // Operational queries: "show me sagas stuck in state X" and
            // "what's been updated in the last N minutes" are both
            // dashboard staples.
            b.HasIndex(x => x.CurrentState);
            b.HasIndex(x => x.UpdatedAtUtc);
        });
    }
}
