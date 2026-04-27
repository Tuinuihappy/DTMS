using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Data;

public class FleetDbContext : DbContext
{
    public const string Schema = "fleet";

    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = null!;
    public DbSet<ChargingPolicy> ChargingPolicies { get; set; } = null!;
    public DbSet<MaintenanceRecord> MaintenanceRecords { get; set; } = null!;
    public DbSet<VehicleGroup> VehicleGroups { get; set; } = null!;

    public FleetDbContext(DbContextOptions<FleetDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasKey(v => v.Id);
            b.Property(v => v.VehicleName).HasMaxLength(100).IsRequired();
            b.Property(v => v.State).HasConversion<string>().HasMaxLength(20);
            b.Property(v => v.GroupIds)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Guid.Parse).ToList())
             .HasColumnName("GroupIds");
            b.Ignore(v => v.DomainEvents);
        });

        modelBuilder.Entity<VehicleType>(b =>
        {
            b.HasKey(vt => vt.Id);
            b.Property(vt => vt.TypeName).HasMaxLength(100).IsRequired();
            b.Property(vt => vt.Capabilities)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
             .HasColumnName("Capabilities");
        });

        modelBuilder.Entity<ChargingPolicy>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Mode).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<MaintenanceRecord>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Reason).HasMaxLength(500);
            b.Property(r => r.Technician).HasMaxLength(200);
            b.Property(r => r.Outcome).HasMaxLength(500);
        });

        modelBuilder.Entity<VehicleGroup>(b =>
        {
            b.HasKey(g => g.Id);
            b.Property(g => g.Name).HasMaxLength(100).IsRequired();
            b.Property(g => g.Description).HasMaxLength(500);
            b.Property(g => g.Tags)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
             .HasColumnName("Tags");
            b.Property(g => g.VehicleIds)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Guid.Parse).ToList())
             .HasColumnName("VehicleIds");
        });

        base.OnModelCreating(modelBuilder);
    }
}
