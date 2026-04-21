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

    public FacilityDbContext(DbContextOptions<FacilityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Map>(builder =>
        {
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
            builder.Property(m => m.Version).HasMaxLength(50).IsRequired();
            // Store MapData as JSONB
            builder.Property(m => m.MapData).HasColumnType("jsonb");

            // Ignore domain properties
            builder.Ignore(m => m.DomainEvents);
            builder.Ignore(m => m.Stations);
            builder.Ignore(m => m.Zones);
            builder.Ignore(m => m.RouteEdges);
        });

        modelBuilder.Entity<Station>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
            builder.OwnsOne(s => s.Coordinate, cb =>
            {
                cb.Property(c => c.X).HasColumnName("CoordinateX");
                cb.Property(c => c.Y).HasColumnName("CoordinateY");
                cb.Property(c => c.Theta).HasColumnName("CoordinateTheta");
            });
        });

        modelBuilder.Entity<Zone>(builder =>
        {
            builder.HasKey(z => z.Id);
            builder.Property(z => z.Name).HasMaxLength(100).IsRequired();
            // In a real app, polygon would be stored via NetTopologySuite or JSON array
            builder.Ignore(z => z.Polygon);
        });

        modelBuilder.Entity<RouteEdge>(builder =>
        {
            builder.HasKey(e => e.Id);
        });

        base.OnModelCreating(modelBuilder);
    }
}
