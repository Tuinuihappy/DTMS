using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Data;

public class FleetDbContext : DbContext
{
    public const string Schema = "fleet";

    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = null!;

    public FleetDbContext(DbContextOptions<FleetDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Vehicle>(builder =>
        {
            builder.HasKey(v => v.Id);
            builder.Property(v => v.VehicleName).HasMaxLength(100).IsRequired();
            builder.Property(v => v.State).HasConversion<string>();

            builder.Ignore(v => v.DomainEvents);
        });

        modelBuilder.Entity<VehicleType>(builder =>
        {
            builder.HasKey(vt => vt.Id);
            builder.Property(vt => vt.TypeName).HasMaxLength(100).IsRequired();
            builder.Ignore(vt => vt.Capabilities); // Simplified for this scaffold
        });

        base.OnModelCreating(modelBuilder);
    }
}
