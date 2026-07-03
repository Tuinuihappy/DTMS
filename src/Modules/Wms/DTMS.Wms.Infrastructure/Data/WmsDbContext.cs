using DTMS.Wms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Wms.Infrastructure.Data;

/// <summary>
/// Isolated context for the external WMS snapshot. Lives in its own
/// <c>wms</c> schema so admin queries can inspect / truncate the cache
/// without collateral damage to Facility. Shares the module-wide
/// <c>public.__EFMigrationsHistory</c> per repo convention.
/// </summary>
public class WmsDbContext : DbContext
{
    public const string Schema = "wms";

    public DbSet<WmsLocation> Locations { get; set; } = null!;

    public WmsDbContext(DbContextOptions<WmsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<WmsLocation>(b =>
        {
            b.HasKey(l => l.Id);

            b.Property(l => l.ExternalId).IsRequired();
            b.HasIndex(l => l.ExternalId).IsUnique();

            b.Property(l => l.LocationCode).HasMaxLength(64).IsRequired();
            // NOTE: the case-insensitive UNIQUE index on LOWER("LocationCode")
            // is created by the migration via raw SQL — EF's fluent HasIndex
            // can't express functional expressions on the key column. See
            // 20260702130000_AddWmsLocations.cs for the CREATE UNIQUE INDEX.

            b.Property(l => l.DisplayName).HasMaxLength(128).IsRequired();

            b.Property(l => l.Type).IsRequired();
            b.Property(l => l.TypeName).HasMaxLength(32);

            b.Property(l => l.IsActive).IsRequired();
            b.HasIndex(l => l.IsActive);

            b.Property(l => l.IsStorageLocation).IsRequired();

            b.Property(l => l.ParentLocationId);
            b.Property(l => l.ParentLocationCode).HasMaxLength(64);
            b.HasIndex(l => l.ParentLocationCode);
            b.Property(l => l.ParentLocationDisplayName).HasMaxLength(128);

            b.Property(l => l.Description).HasMaxLength(500);

            b.Property(l => l.Latitude);
            b.Property(l => l.Longitude);
            b.Property(l => l.ZGpsHeight);
            b.Property(l => l.ZTolerance);
            b.Property(l => l.AccuracyMeters);
            b.Property(l => l.HeightDiff);

            b.Property(l => l.LastSyncedAt).IsRequired();
            b.Property(l => l.CreatedAt).IsRequired();
            b.Property(l => l.RowVersion).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
