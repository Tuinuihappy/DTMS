using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;

public class VendorAdapterDbContext : DbContext
{
    public const string Schema = "vendoradapter";

    public DbSet<ActionCatalogEntry> ActionCatalogEntries { get; set; } = null!;

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
            b.Ignore(e => e.DomainEvents);
        });
    }
}
