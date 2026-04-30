using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Data;

public class FleetDbContext : DbContext
{
    public const string Schema = "fleet";

    private readonly ITenantContext _tenantContext;

    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = null!;
    public DbSet<ChargingPolicy> ChargingPolicies { get; set; } = null!;
    public DbSet<MaintenanceRecord> MaintenanceRecords { get; set; } = null!;
    public DbSet<VehicleGroup> VehicleGroups { get; set; } = null!;
    internal DbSet<VehicleGroupMember> VehicleGroupMembers { get; set; } = null!;

    public FleetDbContext(DbContextOptions<FleetDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasKey(v => v.Id);
            b.Property(v => v.TenantId).IsRequired();
            b.HasQueryFilter(v => v.TenantId == _tenantContext.TenantId);
            b.Property(v => v.VehicleName).HasMaxLength(100).IsRequired();
            b.Property(v => v.State).HasConversion<string>().HasMaxLength(20);
            b.Property(v => v.AdapterKey).HasMaxLength(20).IsRequired().HasDefaultValue("riot3");
            b.Property(v => v.VendorVehicleKey).HasMaxLength(100);
            b.HasIndex(v => new { v.TenantId, v.AdapterKey, v.VendorVehicleKey })
             .IsUnique()
             .HasFilter("\"VendorVehicleKey\" IS NOT NULL");
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
            b.Property(g => g.TenantId).IsRequired();
            b.HasQueryFilter(g => g.TenantId == _tenantContext.TenantId);
            b.Property(g => g.Name).HasMaxLength(100).IsRequired();
            b.Property(g => g.Description).HasMaxLength(500);
            b.Property(g => g.Tags)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
             .HasColumnName("Tags");

            // VehicleIds is now owned by the join table — do not persist on this entity
            b.Ignore(g => g.VehicleIds);

            // xmin = PostgreSQL system column; EF uses it as an optimistic-concurrency token.
            // Any concurrent UPDATE on the same row increments xmin automatically,
            // causing a DbUpdateConcurrencyException for the losing writer.
            b.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
        });

        modelBuilder.Entity<VehicleGroupMember>(b =>
        {
            b.ToTable("VehicleGroupMembers");
            b.HasKey(m => new { m.VehicleGroupId, m.VehicleId });

            // Reverse-lookup: "which groups does vehicle X belong to?"
            b.HasIndex(m => m.VehicleId);

            // Cascade-delete: removing a VehicleGroup removes all its memberships
            b.HasOne<VehicleGroup>()
             .WithMany()
             .HasForeignKey(m => m.VehicleGroupId)
             .OnDelete(DeleteBehavior.Cascade);

            // Referential integrity: VehicleId must exist in fleet.Vehicles
            b.HasOne<Vehicle>()
             .WithMany()
             .HasForeignKey(m => m.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
