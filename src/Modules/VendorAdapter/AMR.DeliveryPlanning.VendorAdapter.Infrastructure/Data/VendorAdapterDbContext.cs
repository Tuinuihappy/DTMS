using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;

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
            b.HasIndex(e => e.ProcessedOnUtc);
        });
    }
}
