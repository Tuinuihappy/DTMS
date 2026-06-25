using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Transport.Manual.Infrastructure.Data;

// Phase 4.1 — Schema for the Manual transport mode.
//
// Naming convention: schema is single-word lowercase ("transportmanual"),
// mirroring "dispatch", "fleet", "facility", "planning". Avoiding the
// underscore form ("transport_manual") matches what shipped for the other
// 7 modules and keeps the cross-context shared __EFMigrationsHistory
// table layout consistent.
public class TransportManualDbContext : DbContext
{
    public const string Schema = "transportmanual";

    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<OperatorCertification> OperatorCertifications => Set<OperatorCertification>();
    public DbSet<OperatorPushSubscription> OperatorPushSubscriptions => Set<OperatorPushSubscription>();
    public DbSet<GeofenceOverrideRequest> GeofenceOverrideRequests => Set<GeofenceOverrideRequest>();
    public DbSet<ManualTripExtension> ManualTripExtensions => Set<ManualTripExtension>();

    public TransportManualDbContext(DbContextOptions<TransportManualDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Operator>(b =>
        {
            b.HasKey(o => o.Id);

            // EmployeeCode is the External Auth user identifier — the
            // natural key. Unique index makes "find operator by JWT
            // subject" O(1) on every login.
            b.Property(o => o.EmployeeCode).HasMaxLength(50).IsRequired();
            b.HasIndex(o => o.EmployeeCode).IsUnique();

            b.Property(o => o.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(o => o.Role).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.Phone).HasMaxLength(50);
            b.Property(o => o.ThumbnailUrl).HasMaxLength(500);
            b.Property(o => o.CreatedAt).IsRequired();
            b.Property(o => o.LastSyncedAt).IsRequired();

            // PrimaryWarehouseId — FK target lives in Facility module's
            // schema; we don't model the cross-schema FK at EF level
            // (would require shared DbContext); the application layer
            // validates referential integrity via IWarehouseLookup.
            b.Property(o => o.PrimaryWarehouseId);

            // CurrentTripId — same cross-module-FK concern, not modelled
            // at the DB level. Index for dispatcher "which trip is
            // operator X on" lookups.
            b.HasIndex(o => o.CurrentTripId)
             .HasFilter("\"CurrentTripId\" IS NOT NULL");

            // Status filter index for "list of active operators" — the
            // operator picker in dispatcher console hits this frequently.
            b.HasIndex(o => o.Status);

            b.Ignore(o => o.DomainEvents);

            // Backing fields for collections (1:N children — separate
            // tables, FK on child).
            b.HasMany(o => o.Certifications)
             .WithOne()
             .HasForeignKey(c => c.OperatorId)
             .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.Certifications).HasField("_certifications").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(o => o.PushSubscriptions)
             .WithOne()
             .HasForeignKey(s => s.OperatorId)
             .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.PushSubscriptions).HasField("_pushSubscriptions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OperatorCertification>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(c => c.RevokedReason).HasMaxLength(500);

            // "Find operators with active hazmat cert" — the assignment
            // policy hits this on every Manual dispatch decision.
            b.HasIndex(c => new { c.OperatorId, c.Type, c.IsActive });
        });

        modelBuilder.Entity<OperatorPushSubscription>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Platform).HasConversion<string>().HasMaxLength(20);

            // Endpoint URLs can run to ~500 chars (FCM tokens are ~163
            // base64 chars; Web Push endpoints vary). 1000 gives headroom.
            b.Property(s => s.Endpoint).HasMaxLength(1000).IsRequired();
            b.Property(s => s.PublicKey).HasMaxLength(200);
            b.Property(s => s.AuthSecret).HasMaxLength(100);
            b.Property(s => s.DeviceLabel).HasMaxLength(100);

            // Lookup by endpoint when the browser re-subscribes (replace
            // existing row instead of duplicating).
            b.HasIndex(s => s.Endpoint).IsUnique();
        });

        modelBuilder.Entity<GeofenceOverrideRequest>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Reason).HasMaxLength(500).IsRequired();
            b.Property(r => r.PhotoUrl).HasMaxLength(500);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.DecisionNote).HasMaxLength(500);
            b.Property(r => r.ReportedLatitude).HasColumnType("double precision");
            b.Property(r => r.ReportedLongitude).HasColumnType("double precision");
            b.Property(r => r.DistanceFromGeofenceM).HasColumnType("double precision");

            // Dispatcher console: "list of pending override requests for
            // my warehouse" — filtering by status is the primary query.
            b.HasIndex(r => r.Status);
            // Per-trip lookup (operator app shows "your override is pending")
            b.HasIndex(r => r.TripId);
            // Expiry sweep — watchdog scans Pending rows past ExpiresAt.
            b.HasIndex(r => new { r.Status, r.ExpiresAt })
             .HasFilter("\"Status\" = 'Pending'");

            b.Ignore(r => r.DomainEvents);
        });

        modelBuilder.Entity<ManualTripExtension>(b =>
        {
            // PK = FK to Dispatch.Trips.Id — same pattern as
            // AmrTripExtension (Phase 3b). Schema-level cross-context FK
            // not declared here for the same reason as Operator.* — would
            // require sharing the DbContext.
            b.HasKey(e => e.TripId);

            // Operator lookup: "which trips is this operator on" (history
            // view in operator app).
            b.HasIndex(e => e.OperatorId);

            b.Property(e => e.PickupPodKey).HasMaxLength(500);
            b.Property(e => e.DropPodKey).HasMaxLength(500);
        });

        base.OnModelCreating(modelBuilder);
    }
}
