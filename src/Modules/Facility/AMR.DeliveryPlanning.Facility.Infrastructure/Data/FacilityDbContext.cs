using AMR.DeliveryPlanning.Facility.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Data;

public class FacilityDbContext : DbContext
{
    public const string Schema = "facility";

    public DbSet<Map> Maps { get; set; } = null!;
    public DbSet<Station> Stations { get; set; } = null!;
    public DbSet<Zone> Zones { get; set; } = null!;
    public DbSet<RouteEdge> RouteEdges { get; set; } = null!;
    public DbSet<TopologyOverlay> TopologyOverlays { get; set; } = null!;
    public DbSet<FacilityResource> FacilityResources { get; set; } = null!;

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

        base.OnModelCreating(modelBuilder);
    }
}
