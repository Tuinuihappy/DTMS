using DTMS.Facility.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Data;

public class FacilityDbContext : DbContext
{
    public const string Schema = "facility";

    public DbSet<Map> Maps { get; set; } = null!;
    public DbSet<Station> Stations { get; set; } = null!;
    public DbSet<Zone> Zones { get; set; } = null!;
    public DbSet<RouteEdge> RouteEdges { get; set; } = null!;
    public DbSet<TopologyOverlay> TopologyOverlays { get; set; } = null!;
    public DbSet<FacilityResource> FacilityResources { get; set; } = null!;
    public DbSet<Shelf> Shelves { get; set; } = null!;
    public DbSet<CarrierTypeProfile> CarrierTypeProfiles { get; set; } = null!;
    public DbSet<LoadUnitProfile> LoadUnitProfiles { get; set; } = null!;
    public DbSet<Warehouse> Warehouses { get; set; } = null!;

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
            b.Ignore(m => m.Zones);
            b.Ignore(m => m.RouteEdges);
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

        modelBuilder.Entity<Zone>(b =>
        {
            b.HasKey(z => z.Id);
            b.Property(z => z.Name).HasMaxLength(100).IsRequired();
            b.Ignore(z => z.Polygon);
        });

        modelBuilder.Entity<RouteEdge>(b =>
        {
            b.HasKey(e => e.Id);
        });

        modelBuilder.Entity<TopologyOverlay>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(o => o.Reason).HasMaxLength(500);
            b.Property(o => o.PolygonJson).HasColumnType("jsonb");
            b.HasIndex(o => new { o.MapId, o.ValidUntil });
        });

        modelBuilder.Entity<FacilityResource>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.ResourceKey).HasMaxLength(100).IsRequired();
            b.Property(r => r.ResourceType).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.VendorRef).HasMaxLength(200);
            b.Property(r => r.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Shelf>(b =>
        {
            b.HasKey(s => s.Id);
            b.Ignore(s => s.DomainEvents);
            b.Property(s => s.Rfid).HasMaxLength(100).IsRequired();
            b.HasIndex(s => s.Rfid).IsUnique();
            b.HasIndex(s => s.MapId);
            b.Property(s => s.MaxWeightKg).IsRequired();
            b.Property(s => s.MaxSlots).IsRequired();
            b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
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

        modelBuilder.Entity<Warehouse>(b =>
        {
            b.HasKey(w => w.Id);
            b.Property(w => w.Code).HasMaxLength(50).IsRequired();
            b.HasIndex(w => w.Code).IsUnique();
            b.Property(w => w.Name).HasMaxLength(200).IsRequired();

            // Owned value objects — explicit HasColumnName on every property
            // per memory feedback_owned_entity_column_name: without these EF
            // generates "Location_Lat" / "Address_Street" style names that
            // are awkward in raw SQL and migrations.
            b.OwnsOne(w => w.Location, lb =>
            {
                lb.Property(l => l.Lat).HasColumnName("LocationLat").IsRequired();
                lb.Property(l => l.Lng).HasColumnName("LocationLng").IsRequired();
            });
            b.Navigation(w => w.Location).IsRequired();

            b.OwnsOne(w => w.Address, ab =>
            {
                ab.Property(a => a.Street).HasColumnName("AddressStreet").HasMaxLength(500).IsRequired();
                ab.Property(a => a.City).HasColumnName("AddressCity").HasMaxLength(100);
                ab.Property(a => a.State).HasColumnName("AddressState").HasMaxLength(100);
                ab.Property(a => a.PostalCode).HasColumnName("AddressPostalCode").HasMaxLength(20);
                ab.Property(a => a.Country).HasColumnName("AddressCountry").HasMaxLength(100);
            });
            b.Navigation(w => w.Address).IsRequired();

            b.OwnsOne(w => w.Hours, hb =>
            {
                hb.Property(h => h.MondayOpen).HasColumnName("HoursMondayOpen");
                hb.Property(h => h.MondayClose).HasColumnName("HoursMondayClose");
                hb.Property(h => h.TuesdayOpen).HasColumnName("HoursTuesdayOpen");
                hb.Property(h => h.TuesdayClose).HasColumnName("HoursTuesdayClose");
                hb.Property(h => h.WednesdayOpen).HasColumnName("HoursWednesdayOpen");
                hb.Property(h => h.WednesdayClose).HasColumnName("HoursWednesdayClose");
                hb.Property(h => h.ThursdayOpen).HasColumnName("HoursThursdayOpen");
                hb.Property(h => h.ThursdayClose).HasColumnName("HoursThursdayClose");
                hb.Property(h => h.FridayOpen).HasColumnName("HoursFridayOpen");
                hb.Property(h => h.FridayClose).HasColumnName("HoursFridayClose");
                hb.Property(h => h.SaturdayOpen).HasColumnName("HoursSaturdayOpen");
                hb.Property(h => h.SaturdayClose).HasColumnName("HoursSaturdayClose");
                hb.Property(h => h.SundayOpen).HasColumnName("HoursSundayOpen");
                hb.Property(h => h.SundayClose).HasColumnName("HoursSundayClose");
            });
            b.Navigation(w => w.Hours).IsRequired();

            // PrimaryContact is OPTIONAL — owned-entity nullability via
            // navigation not required. All columns nullable at DB level
            // even though Name/Phone are required when contact exists.
            b.OwnsOne(w => w.PrimaryContact, cb =>
            {
                cb.Property(c => c.Name).HasColumnName("ContactName").HasMaxLength(200);
                cb.Property(c => c.Phone).HasColumnName("ContactPhone").HasMaxLength(50);
                cb.Property(c => c.Email).HasColumnName("ContactEmail").HasMaxLength(200);
            });

            b.Property(w => w.GeofenceRadiusM);
            b.Property(w => w.GeofenceAreaWkt).HasMaxLength(5000);

            // ServiceModes (List<TransportMode>) → jsonb. Stored as string
            // array so the JSON is human-readable in queries / pg_dump.
            // Typed Property<T> form (not Property(typeof, name)) — the
            // untyped overload returns a non-generic PropertyBuilder which
            // would force HasConversion to take Type arguments instead of
            // the strongly-typed Func<T,U> lambdas we want.
            b.Property<List<DTMS.DeliveryOrder.Domain.Enums.TransportMode>>("_serviceModes")
                .HasField("_serviceModes")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasColumnName("ServiceModes")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(
                        v.Select(m => m.ToString()).ToList(),
                        (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                            v, (System.Text.Json.JsonSerializerOptions?)null)!
                        .Select(s => Enum.Parse<DTMS.DeliveryOrder.Domain.Enums.TransportMode>(s))
                        .ToList())
                .IsRequired();
            // Hide the public read-only navigation (it's IReadOnlyCollection
            // backed by _serviceModes) from EF so it doesn't try to map twice.
            b.Ignore(w => w.ServiceModes);

            b.Property(w => w.IsActive).HasDefaultValue(true).IsRequired();
            b.Property(w => w.CreatedAt).IsRequired();
            b.Property(w => w.UpdatedAt);

            b.HasIndex(w => w.IsActive);

            b.Ignore(w => w.DomainEvents);
        });

        base.OnModelCreating(modelBuilder);
    }
}
