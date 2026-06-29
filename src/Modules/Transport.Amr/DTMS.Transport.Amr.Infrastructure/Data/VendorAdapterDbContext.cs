using DTMS.SharedKernel.Outbox;
using DTMS.Transport.Abstractions.Models;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Transport.Amr.Infrastructure.Data;

public class VendorAdapterDbContext : DbContext
{
    public const string Schema = "vendoradapter";

    public DbSet<ActionCatalogEntry> ActionCatalogEntries { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public VendorAdapterDbContext(DbContextOptions<VendorAdapterDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ActionCatalogEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.VehicleTypeKey).HasMaxLength(50).IsRequired();
            b.Property(e => e.CanonicalAction).HasMaxLength(50).IsRequired();
            b.Property(e => e.AdapterKey).HasMaxLength(50).IsRequired();
            b.Property(e => e.VendorParamsJson).HasColumnType("jsonb");
            b.HasIndex(e => new { e.VehicleTypeKey, e.CanonicalAction }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(e => e.Id);
            b.Property(e => e.Type).HasMaxLength(500).IsRequired();
            b.Property(e => e.Content).HasColumnType("text").IsRequired();
            b.Property(e => e.RetryCount).HasDefaultValue(0);
            b.HasIndex(e => e.ProcessedOnUtc);
            b.HasIndex(e => e.NextRetryAtUtc);
            // Phase S.3 / S.3.1b — PartitionKey + CorrelationId are
            // mapped only on the central OutboxDbContext (outbox schema).
            // Module outbox tables don't have the columns; ignore so EF
            // doesn't SELECT them.
            b.Ignore(e => e.PartitionKey);
            b.Ignore(e => e.CorrelationId);
        });
    }
}
