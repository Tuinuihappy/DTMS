using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Infrastructure.Projections;
using DTMS.SharedKernel.Outbox;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Data;

public class FleetDbContext : DbContext
{
    public const string Schema = "fleet";

    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = null!;
    public DbSet<ChargingPolicy> ChargingPolicies { get; set; } = null!;
    public DbSet<MaintenanceRecord> MaintenanceRecords { get; set; } = null!;
    public DbSet<VehicleGroup> VehicleGroups { get; set; } = null!;
    // ADR-019 — carrier catalogue, moved here from Facility in P0.2.
    public DbSet<CarrierType> CarrierTypes { get; set; } = null!;
    // ADR-019 P1 — the physical carriers themselves + their maintenance history.
    public DbSet<Carrier> Carriers { get; set; } = null!;
    public DbSet<CarrierMaintenanceLog> CarrierMaintenanceLogs { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    internal DbSet<VehicleGroupMember> VehicleGroupMembers { get; set; } = null!;

    // ── Phase P3.2 — Fleet projections ───────────────────────────────────
    public DbSet<VehicleStateHistoryRow> VehicleStateHistory => Set<VehicleStateHistoryRow>();
    public DbSet<FleetUtilizationHourlyRow> FleetUtilizationHourly => Set<FleetUtilizationHourlyRow>();
    public DbSet<InboxMessage> ProjectionInbox => Set<InboxMessage>();

    public FleetDbContext(DbContextOptions<FleetDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasKey(v => v.Id);
            b.Property(v => v.VehicleName).HasMaxLength(100).IsRequired();
            b.Property(v => v.State).HasConversion<string>().HasMaxLength(20);
            b.Property(v => v.AdapterKey).HasMaxLength(20).IsRequired().HasDefaultValue("riot3");
            b.Property(v => v.VendorVehicleKey).HasMaxLength(100);
            b.HasIndex(v => new { v.AdapterKey, v.VendorVehicleKey })
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

        // ADR-019 — config carried over verbatim from FacilityDbContext apart
        // from the AMRCapability → AmrCapability property rename, so the
        // relocated table keeps the exact shape migration 20260506082818 built.
        modelBuilder.Entity<CarrierType>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Code).HasMaxLength(50).IsRequired();
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(c => c.AmrCapability).HasMaxLength(50).IsRequired();
            b.Property(c => c.MaxWeightKg);
            b.Property(c => c.MaxSlots);
            b.Property(c => c.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Carrier>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.CarrierCode).HasMaxLength(50).IsRequired();
            // Unconditional, not filtered on RetiredAt: a code identifies one
            // physical carrier forever, so "CART-0001 ran 47 trips" can never
            // become ambiguous. Deleting frees the code, but only carriers with
            // no history at all can be deleted — see Carrier.CanDelete.
            b.HasIndex(c => c.CarrierCode).IsUnique();

            b.Property(c => c.Barcode).HasMaxLength(100);
            b.HasIndex(c => c.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");

            b.Property(c => c.DisplayName).HasMaxLength(200);
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(c => c.MaintenanceReason).HasMaxLength(500);
            b.Property(c => c.CurrentLocationCode).HasMaxLength(100);
            b.Property(c => c.RetireReason).HasMaxLength(500);
            b.Property(c => c.CreatedBy).HasMaxLength(200);
            b.Property(c => c.ModifiedBy).HasMaxLength(200);

            // Backs the list page's status/type filter.
            b.HasIndex(c => new { c.Status, c.CarrierTypeId });

            // Ordinary FK to the principal's PRIMARY key. Pointing it at
            // CarrierTypes.Code instead would need an alternate key — EF models
            // that as a second UNIQUE constraint, which PostgreSQL implements
            // with a second index alongside IX_CarrierTypes_Code, and no other
            // table in this solution uses HasAlternateKey at all.
            b.HasOne<CarrierType>()
             .WithMany()
             .HasForeignKey(c => c.CarrierTypeId)
             .OnDelete(DeleteBehavior.Restrict);

            b.Ignore(c => c.DomainEvents);
            // xmin — same optimistic-concurrency posture as VehicleGroup and Trip.
            b.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
        });

        modelBuilder.Entity<CarrierMaintenanceLog>(b =>
        {
            b.ToTable("CarrierMaintenanceLog");
            b.HasKey(l => l.Id);
            b.Property(l => l.Reason).HasMaxLength(500).IsRequired();
            b.Property(l => l.StartedBy).HasMaxLength(200).IsRequired();
            b.Property(l => l.EndedBy).HasMaxLength(200);
            b.Property(l => l.Outcome).HasMaxLength(500);

            b.HasOne<Carrier>()
             .WithMany()
             .HasForeignKey(l => l.CarrierId)
             .OnDelete(DeleteBehavior.Cascade);

            // Newest-first history for one carrier.
            b.HasIndex(l => new { l.CarrierId, l.StartedAt }).IsDescending(false, true);
            // At most one open episode per carrier — enforced here rather than
            // by a read-then-write check that two callers could both pass.
            b.HasIndex(l => l.CarrierId).IsUnique().HasFilter("\"EndedAt\" IS NULL");
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(e => e.Id);
            b.Property(e => e.Type).HasMaxLength(500).IsRequired();
            b.Property(e => e.Content).HasColumnType("text").IsRequired();
            b.Property(e => e.RetryCount).HasDefaultValue(0);
            b.HasIndex(e => e.ProcessedOnUtc);
            b.HasIndex(e => e.NextRetryAtUtc);
            // Phase S.3 / S.3.1b — PartitionKey + CorrelationId are
            // mapped only on the central OutboxDbContext; module outbox
            // tables don't have the columns.
            b.Ignore(e => e.PartitionKey);
            b.Ignore(e => e.CorrelationId);
            // Phase S.5 — callback route + order/trip linkage columns are
            // mapped only on the central OutboxDbContext; this module's
            // table doesn't have them.
            b.Ignore(e => e.CallbackPath);
            b.Ignore(e => e.CallbackMethod);
            b.Ignore(e => e.RelatedOrderId);
            b.Ignore(e => e.RelatedTripId);
            // Phase O4 — W3C traceparent captured at write time.
            b.Property(e => e.TraceParent).HasMaxLength(55);
        });

        // ── Phase P3.2 — projection_inbox + read models ────────────────
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("ProjectionInbox", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.ProjectorName).HasMaxLength(200).IsRequired();
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.ProcessedAtUtc).IsRequired();
            b.HasIndex(e => new { e.ProjectorName, e.EventId }).IsUnique();
        });

        modelBuilder.Entity<VehicleStateHistoryRow>(b =>
        {
            b.ToTable("VehicleStateHistory", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.VehicleId).IsRequired();
            b.Property(e => e.FromState).HasMaxLength(30);
            b.Property(e => e.ToState).HasMaxLength(30).IsRequired();
            b.Property(e => e.BatteryLevel);
            b.Property(e => e.CurrentNodeId);
            b.Property(e => e.OccurredAt).IsRequired();
            b.HasIndex(e => new { e.VehicleId, e.OccurredAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.ToState, e.OccurredAt });
        });

        modelBuilder.Entity<FleetUtilizationHourlyRow>(b =>
        {
            b.ToTable("FleetUtilizationHourly", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.BucketHour).IsRequired();
            b.HasIndex(e => e.BucketHour).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
