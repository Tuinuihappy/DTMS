using DTMS.Facility.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Data;

public class FacilityDbContext : DbContext
{
    public const string Schema = "facility";

    public DbSet<Map> Maps { get; set; } = null!;
    public DbSet<Station> Stations { get; set; } = null!;
    public DbSet<CarrierTypeProfile> CarrierTypeProfiles { get; set; } = null!;
    public DbSet<LoadUnitProfile> LoadUnitProfiles { get; set; } = null!;

    public FacilityDbContext(DbContextOptions<FacilityDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Map>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.Name).HasMaxLength(100).IsRequired();
            b.Property(m => m.Version).HasMaxLength(50).IsRequired();
            b.Property(m => m.MapData).HasColumnType("jsonb");
            b.Property(m => m.VendorRef).HasMaxLength(200);
            b.HasIndex(m => m.VendorRef).IsUnique().HasFilter("\"VendorRef\" IS NOT NULL");
            b.Ignore(m => m.DomainEvents);
            b.Ignore(m => m.Stations);
        });

        modelBuilder.Entity<Station>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(100).IsRequired();
            b.Property(s => s.Type).HasConversion<string>().HasMaxLength(20);
            b.OwnsOne(s => s.Coordinate, cb =>
            {
                cb.Property(c => c.X).HasColumnName("CoordinateX");
                cb.Property(c => c.Y).HasColumnName("CoordinateY");
                cb.Property(c => c.Theta).HasColumnName("CoordinateTheta");
            });
            b.Property(s => s.CompatibleVehicleTypes)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
             .HasColumnName("CompatibleVehicleTypes");
            b.Property(s => s.VendorRef).HasMaxLength(200);
            b.HasIndex(s => new { s.MapId, s.VendorRef }).IsUnique().HasFilter("\"VendorRef\" IS NOT NULL");
            b.Property(s => s.Code).HasMaxLength(50);
            b.HasIndex(s => new { s.MapId, s.Code }).IsUnique().HasFilter("\"Code\" IS NOT NULL");
            b.Property(s => s.IsActive).HasDefaultValue(true).IsRequired();
            b.HasIndex(s => new { s.MapId, s.IsActive });
            b.Property(s => s.ManualOverrideOffline).HasDefaultValue(false).IsRequired();
            b.Property(s => s.ManualOverrideReason).HasMaxLength(500);
            b.Property(s => s.ManualOverrideBy).HasMaxLength(200);
            b.Property(s => s.ManualOverrideAt);
            b.Property(s => s.ManualOverrideExpiresAt);
            b.HasIndex(s => s.ManualOverrideExpiresAt).HasFilter("\"ManualOverrideOffline\" = true");

            // Vendor action map (intent → StationAction). Stored as a single
            // jsonb document — keeps RIOT3 ACT-mission config close to the
            // station record without a separate child table. The value
            // converter serializes the whole IReadOnlyDictionary at once.
            b.Property(s => s.Actions)
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(
                            v,
                            (System.Text.Json.JsonSerializerOptions?)null),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : (IReadOnlyDictionary<string, DTMS.Facility.Domain.ValueObjects.StationAction>?)
                          System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DTMS.Facility.Domain.ValueObjects.StationAction>>(
                              v,
                              (System.Text.Json.JsonSerializerOptions?)null))
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<CarrierTypeProfile>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Code).HasMaxLength(50).IsRequired();
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(c => c.AMRCapability).HasMaxLength(50).IsRequired();
            b.Property(c => c.MaxWeightKg);
            b.Property(c => c.MaxSlots);
            b.Property(c => c.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<LoadUnitProfile>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(50).IsRequired();
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(p => p.LengthMm).IsRequired();
            b.Property(p => p.WidthMm).IsRequired();
            b.Property(p => p.HeightMm).IsRequired();
            b.Property(p => p.MaxGrossWeightKg).IsRequired();
            b.Property(p => p.CarrierTypeCode).HasMaxLength(50).IsRequired();
            b.HasIndex(p => p.CarrierTypeCode);
        });

        base.OnModelCreating(modelBuilder);
    }
}
