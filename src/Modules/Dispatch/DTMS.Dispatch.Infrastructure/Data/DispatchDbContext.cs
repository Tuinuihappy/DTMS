using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Infrastructure.Projections;
using DTMS.SharedKernel.Outbox;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Dispatch.Infrastructure.Data;

public class DispatchDbContext : DbContext
{
    public const string Schema = "dispatch";

    public DbSet<Trip> Trips { get; set; } = null!;
    public DbSet<ExecutionEvent> ExecutionEvents { get; set; } = null!;
    public DbSet<TripException> TripExceptions { get; set; } = null!;
    public DbSet<ProofOfDelivery> ProofsOfDelivery { get; set; } = null!;
    public DbSet<ShelfManifest> ShelfManifests { get; set; } = null!;
    public DbSet<TripRetryEvent> TripRetryEvents { get; set; } = null!;
    public DbSet<TripMissionEvent> TripMissionEvents { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // ── Phase P1 — Event Projection read models ──────────────────────────
    public DbSet<TripStatusHistoryRow> TripStatusHistory => Set<TripStatusHistoryRow>();
    public DbSet<InboxMessage> ProjectionInbox => Set<InboxMessage>();

    // ── Phase P5.2 — BI fact table (cross-cutting bi schema, owned here) ──
    public DbSet<TripFactsRow> TripFacts => Set<TripFactsRow>();

    // ── Phase P5.3 — TripItems read model (Trip ↔ Item binding) ───────────
    public DbSet<TripItemsRow> TripItems => Set<TripItemsRow>();

    public DispatchDbContext(DbContextOptions<DispatchDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Trip>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(t => t.UpperKey).HasMaxLength(80).IsRequired();
            // Phase 3b — vendor fields moved off Trip into AmrTripExtension
            // (mapped below). Trip core stays mode-agnostic; AMR extension
            // is a 1:0..1 navigation, lazily created.
            builder.Property(t => t.FailureReason).HasMaxLength(1000);
            builder.Property(t => t.AttemptNumber).HasDefaultValue(1);
            builder.HasIndex(t => t.PreviousAttemptId)
                .HasFilter("\"PreviousAttemptId\" IS NOT NULL");

            // Vendor detail snapshots — lifted columns are indexable;
            // raw JSONB blobs use Postgres jsonb so JSON path queries
            // work and TOAST compresses large payloads automatically.
            builder.Property(t => t.TemplateNameAtDispatch).HasMaxLength(200);
            builder.Property(t => t.VendorRequestSnapshot).HasColumnType("jsonb");
            builder.Property(t => t.VendorFinalSnapshot).HasColumnType("jsonb");
            // Phase 2.5 — warehouse Ids snapshotted at dispatch (per ADR-002).
            // Nullable for now; existing CreateForEnvelope callers don't pass
            // them yet. Phase 2.6 wires resolution at the command-handler
            // layer so every new Trip carries both Ids.
            // WMS PR-2 — Manual/Fleet trips snapshot WMS location Ids.
            // AMR trips leave these NULL (station-based).
            builder.Property(t => t.PickupWmsLocationId);
            builder.Property(t => t.DropWmsLocationId);
            builder.Property(t => t.PickupStationId);
            builder.Property(t => t.DropStationId);
            // WMS PR-4b — pool dispatch tracking. The partial index that
            // powers the "available trips" pool query lives in the
            // migration (EF fluent HasIndex can't express the composite
            // filter WHERE Status='Dispatched' AND ClaimedByOperatorId IS NULL).
            builder.Property(t => t.ClaimedByOperatorId);
            builder.Property(t => t.ClaimedAt);
            builder.Property(t => t.DispatchedAt);
            // UpperKey is the RIOT3 correlation key (and unique). Legacy
            // job/task trips (which had null UpperKey) were dropped in
            // Phase b7 — all surviving rows are envelope-dispatched.
            builder.HasIndex(t => t.UpperKey).IsUnique();
            // Phase b8 — reverse lookup Job → Trip(s). Filter out the
            // sentinel empty Guid used by pre-b8 envelope rows so the
            // index stays small.
            builder.HasIndex(t => t.JobId)
                .HasFilter("\"JobId\" != '00000000-0000-0000-0000-000000000000'");
            builder.Ignore(t => t.DomainEvents);

            builder.HasMany(t => t.Events)
                   .WithOne()
                   .HasForeignKey(e => e.TripId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(t => t.Events)
                   .HasField("_events")
                   .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.HasMany(t => t.Exceptions)
                   .WithOne()
                   .HasForeignKey(e => e.TripId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(t => t.Exceptions)
                   .HasField("_exceptions")
                   .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.HasMany(t => t.ProofsOfDelivery)
                   .WithOne()
                   .HasForeignKey(p => p.TripId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(t => t.ProofsOfDelivery)
                   .HasField("_proofs")
                   .UsePropertyAccessMode(PropertyAccessMode.Field);

            // Phase 3b — 1:0..1 AMR extension. Cascade so the extension
            // disappears when the Trip is deleted (cleanup-only path —
            // production code never deletes Trips).
            builder.HasOne(t => t.AmrExtension)
                   .WithOne()
                   .HasForeignKey<AmrTripExtension>(e => e.TripId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(t => t.AmrExtension);
        });

        modelBuilder.Entity<AmrTripExtension>(builder =>
        {
            // PK = FK = TripId (1:0..1 share key — no separate Id needed).
            builder.HasKey(e => e.TripId);
            // EF's auto-pluralised table name would be "AmrTripExtension"
            // (singular) because the entity has no DbSet<> on the context;
            // the migration creates "AmrTripExtensions" so pin it explicitly.
            builder.ToTable("AmrTripExtensions", "dispatch");
            builder.Property(e => e.VendorOrderKey).HasMaxLength(100);
            builder.Property(e => e.VendorVehicleKey).HasMaxLength(100);
            builder.Property(e => e.VendorVehicleName).HasMaxLength(100);
            // VendorPauseSource — string-coded so DB reads stay intelligible
            // ("Operator" / "Vendor") and the enum can grow without a
            // re-numbering migration. Nullable; only set when paused.
            builder.Property(e => e.VendorPauseSource).HasConversion<string>().HasMaxLength(20);

            // Phase 3d — vehicle reassignment history. Cascade delete so
            // dropping the extension also drops its history (extension
            // lifecycle = trip lifecycle).
            builder.HasMany(e => e.VehicleAssignments)
                   .WithOne()
                   .HasForeignKey(a => a.TripId)
                   .HasPrincipalKey(e => e.TripId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(e => e.VehicleAssignments)
                   .HasField("_vehicleAssignments")
                   .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AmrVehicleAssignment>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.ToTable("AmrVehicleAssignments", "dispatch");
            builder.Property(a => a.VendorVehicleKey).HasMaxLength(100).IsRequired();
            builder.Property(a => a.VendorVehicleName).HasMaxLength(100);
            builder.Property(a => a.Source).HasMaxLength(40).IsRequired();
            // Composite uniqueness on (TripId, Sequence) — prevents
            // double-write race conditions (two webhooks for the same
            // reassignment land in parallel; one wins, the other gets a
            // PK violation and the consumer retries).
            builder.HasIndex(a => new { a.TripId, a.Sequence }).IsUnique();
        });

        modelBuilder.Entity<ExecutionEvent>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.EventType).HasMaxLength(50);
            builder.Property(e => e.Details).HasMaxLength(500);
        });

        modelBuilder.Entity<TripRetryEvent>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.RetrySource).HasMaxLength(20).IsRequired();
            builder.Property(e => e.RetriedBy).HasMaxLength(100);
            builder.Property(e => e.RetryReason).HasMaxLength(1000);
            builder.Property(e => e.OriginalStatus).HasMaxLength(20).IsRequired();
            // Indexes for the common ops/compliance queries.
            builder.HasIndex(e => e.OriginalTripId);
            builder.HasIndex(e => e.DeliveryOrderId);
            builder.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<TripMissionEvent>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.MissionKey).HasMaxLength(100).IsRequired();
            builder.Property(e => e.MissionType).HasMaxLength(20).IsRequired();
            builder.Property(e => e.State).HasMaxLength(20).IsRequired();
            builder.Property(e => e.StationName).HasMaxLength(100);
            builder.Property(e => e.ActionName).HasMaxLength(100);
            builder.Property(e => e.ActionType).HasMaxLength(50);
            builder.Property(e => e.ResultCode).HasMaxLength(20);
            // Idempotency: webhook + reconciler can both write without coordination.
            builder.HasIndex(e => new { e.TripId, e.MissionKey, e.State }).IsUnique();
            builder.HasIndex(e => new { e.TripId, e.MissionIndex });
        });

        modelBuilder.Entity<TripException>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Code).HasMaxLength(100).IsRequired();
            builder.Property(e => e.Severity).HasMaxLength(20).IsRequired();
            builder.Property(e => e.Detail).HasMaxLength(1000);
            builder.Property(e => e.Resolution).HasMaxLength(1000);
            builder.Property(e => e.ResolvedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<ProofOfDelivery>(builder =>
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.PhotoUrl).HasMaxLength(1000);
            builder.Property(p => p.Notes).HasMaxLength(500);
            builder.Property(p => p.ScannedIds)
                   .HasConversion(
                       v => string.Join(',', v),
                       v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());
        });

        modelBuilder.Entity<ShelfManifest>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.HasIndex(s => s.JobId).IsUnique();
            builder.HasIndex(s => s.TripId).HasFilter("\"TripId\" IS NOT NULL");
            builder.Property(s => s.ShelfRfid).HasMaxLength(100).IsRequired();
            builder.Property(s => s.PackageBarcodes)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .HasColumnType("text");
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("OutboxMessages");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Type).HasMaxLength(500).IsRequired();
            builder.Property(e => e.Content).HasColumnType("text").IsRequired();
            builder.Property(e => e.RetryCount).HasDefaultValue(0);
            builder.HasIndex(e => e.ProcessedOnUtc);
            builder.HasIndex(e => e.NextRetryAtUtc);
            // Phase S.3 / S.3.1b — PartitionKey + CorrelationId are
            // mapped only on the central OutboxDbContext; this module's
            // table doesn't have the columns.
            builder.Ignore(e => e.PartitionKey);
            builder.Ignore(e => e.CorrelationId);
            // Phase O4 — W3C traceparent captured at write time.
            builder.Property(e => e.TraceParent).HasMaxLength(55);
        });

        // ── Phase P1 — projection_inbox (idempotency bookkeeping) ──────
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("ProjectionInbox", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.ProjectorName).HasMaxLength(200).IsRequired();
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.ProcessedAtUtc).IsRequired();
            b.HasIndex(e => new { e.ProjectorName, e.EventId }).IsUnique();
        });

        // ── Phase P1 — TripStatusHistory read model ────────────────────
        // Written exclusively by TripStatusHistoryProjector. DeliveryOrderId
        // + JobId are nullable to accommodate TripPaused/TripResumed events
        // whose domain payload doesn't carry them.
        modelBuilder.Entity<TripStatusHistoryRow>(b =>
        {
            b.ToTable("TripStatusHistory", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.TripId).IsRequired();
            b.Property(e => e.DeliveryOrderId);
            b.Property(e => e.JobId);
            b.Property(e => e.FromStatus).HasMaxLength(30);
            b.Property(e => e.ToStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.OccurredAt).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(2000);
            b.HasIndex(e => new { e.TripId, e.OccurredAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.DeliveryOrderId, e.OccurredAt });
            b.HasIndex(e => new { e.ToStatus, e.OccurredAt });
        });

        // ── Phase P5.2 — bi.TripFacts BI fact table ────────────────────
        // Mirror of OrderFacts shape. Generated KPI columns
        // (TimeToStartSec, TimeToCompleteSec, SlaCompleteBreached) live
        // in the migration as STORED columns; EF reads them only.
        modelBuilder.Entity<TripFactsRow>(b =>
        {
            b.ToTable("TripFacts", "bi");
            b.HasKey(e => e.TripId);
            b.Property(e => e.VendorUpperKey).HasMaxLength(100);
            b.Property(e => e.VendorVehicleKey).HasMaxLength(100);
            b.Property(e => e.FinalStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.FailureReason).HasMaxLength(2000);

            b.Property(e => e.TimeToStartSec)
                .HasColumnType("integer")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            b.Property(e => e.TimeToCompleteSec)
                .HasColumnType("integer")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            b.Property(e => e.SlaCompleteBreached)
                .HasColumnType("boolean")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

            b.HasIndex(e => e.CreatedAt).IsDescending(true);
            b.HasIndex(e => new { e.VendorUpperKey, e.CreatedAt }).IsDescending(false, true);
            // Vehicle performance report groups + filters by this column.
            b.HasIndex(e => new { e.VendorVehicleKey, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.FinalStatus, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => e.DeliveryOrderId);
            b.HasIndex(e => e.SlaCompleteBreached)
                .HasFilter("\"SlaCompleteBreached\" = true");
        });

        // ── Phase P5.3 — dispatch.TripItems read model ─────────────────
        // Written exclusively by TripItemsProjector. Composite PK on
        // (TripId, ItemPk) so a duplicate replay can't insert twice.
        // EventId is UNIQUE for replay safety (single event writes N rows
        // but the EventId is the same on all of them — UNIQUE applies to
        // the inbox row instead; we keep the column here for forensics).
        modelBuilder.Entity<TripItemsRow>(b =>
        {
            b.ToTable("TripItems", Schema);
            b.HasKey(e => new { e.TripId, e.ItemPk });
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.DeliveryOrderId).IsRequired();
            b.Property(e => e.OrderRef).HasMaxLength(100).IsRequired();
            b.Property(e => e.OrderStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.LotNo).HasMaxLength(200).IsRequired();
            b.Property(e => e.ItemSeq).IsRequired();
            b.Property(e => e.ItemStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.PickupCode).HasMaxLength(50);
            b.Property(e => e.DropCode).HasMaxLength(50);
            b.Property(e => e.WeightKg);
            b.Property(e => e.Description).HasMaxLength(500);
            b.Property(e => e.QuantityValue);
            b.Property(e => e.QuantityUom).HasMaxLength(30);
            b.Property(e => e.OrderTransportMode).HasMaxLength(20);
            b.Property(e => e.BoundAt).IsRequired();
            b.Property(e => e.LastEventAt).IsRequired();

            // Operator drawer drives traffic by TripId; covered by the PK.
            // Reverse-lookup indexes for "which trips ever carried this order/lot".
            b.HasIndex(e => e.DeliveryOrderId);
            b.HasIndex(e => e.OrderRef);
            b.HasIndex(e => e.LotNo);
        });
    }
}
