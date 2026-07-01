using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Outbox;

public class OutboxDbContext : DbContext
{
    public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Phase O3 — terminal-failure records moved here from per-module
    // OutboxMessages tables. See DeadLetterMessage docs.
    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("outbox");

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasMaxLength(500).IsRequired();
            e.Property(m => m.Content).IsRequired();
            e.Property(m => m.OccurredOnUtc).IsRequired();
            e.Property(m => m.RetryCount).HasDefaultValue(0);
            e.Property(m => m.PartitionKey).HasMaxLength(50);
            e.Property(m => m.CorrelationId);
            e.HasIndex(m => m.ProcessedOnUtc);
            e.HasIndex(m => m.NextRetryAtUtc);
            // Phase O4 — W3C traceparent captured at write time.
            e.Property(m => m.TraceParent).HasMaxLength(55);
        });

        modelBuilder.Entity<DeadLetterMessage>(e =>
        {
            e.ToTable("DeadLetterMessages");
            e.HasKey(m => m.Id);
            e.Property(m => m.OriginalOutboxId).IsRequired();
            e.Property(m => m.Source).HasMaxLength(50).IsRequired();
            e.Property(m => m.Type).HasMaxLength(500).IsRequired();
            e.Property(m => m.Content).IsRequired();
            e.Property(m => m.OccurredOnUtc).IsRequired();
            e.Property(m => m.FirstFailedOnUtc).IsRequired();
            e.Property(m => m.LastFailedOnUtc).IsRequired();
            e.Property(m => m.RetryCount).IsRequired();
            e.Property(m => m.LastError).HasMaxLength(4000);
            e.Property(m => m.TraceParent).HasMaxLength(55);
            // Idempotency guard — repeated moves for the same original
            // outbox row are no-ops. See DeadLetterMessage docs.
            e.HasIndex(m => m.OriginalOutboxId).IsUnique();
            e.HasIndex(m => m.LastFailedOnUtc).IsDescending();
            e.HasIndex(m => m.Source);
        });
    }
}
