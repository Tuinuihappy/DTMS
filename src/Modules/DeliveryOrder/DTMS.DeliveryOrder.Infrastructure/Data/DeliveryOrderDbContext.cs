using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.DeliveryOrder.Infrastructure.Projections;
using DTMS.SharedKernel.Outbox;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Data;

public class DeliveryOrderDbContext : DbContext
{
    public const string Schema = "deliveryorder";

    public DbSet<Domain.Entities.DeliveryOrder> DeliveryOrders { get; set; } = null!;
    public DbSet<Item> Items { get; set; } = null!;
    public DbSet<ItemPodEvent> ItemPodEvents { get; set; } = null!;
    public DbSet<OrderAmendment> OrderAmendments { get; set; } = null!;
    public DbSet<OrderAuditEvent> OrderAuditEvents { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // ── Phase P1 — Event Projection read models ──────────────────────────
    public DbSet<OrderStatusHistoryRow> OrderStatusHistory => Set<OrderStatusHistoryRow>();
    public DbSet<InboxMessage> ProjectionInbox => Set<InboxMessage>();

    // ── Phase P2 — Unified order activity timeline ───────────────────────
    public DbSet<OrderActivityRow> OrderActivity => Set<OrderActivityRow>();

    // ── Phase P3 — Dashboard read models ────────────────────────────────
    public DbSet<OrderFunnelHourlyRow> OrderFunnelHourly => Set<OrderFunnelHourlyRow>();

    // ── Phase P4 — Denormalized list/search view ───────────────────────
    public DbSet<OrderListViewRow> OrderListView => Set<OrderListViewRow>();

    // ── Phase P5 — BI fact table (cross-cutting bi schema, owned here) ──
    public DbSet<OrderFactsRow> OrderFacts => Set<OrderFactsRow>();

    public DeliveryOrderDbContext(DbContextOptions<DeliveryOrderDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Domain.Entities.DeliveryOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.OrderRef).HasMaxLength(200).IsRequired();
            b.Property(o => o.CreatedDate).IsRequired();
            b.Property(o => o.UpdatedDate);
            b.Property(o => o.TotalWeightKg).IsRequired();
            b.Property(o => o.TotalQuantity).IsRequired();
            b.Property(o => o.TotalItems).IsRequired();
            b.Property(o => o.SourceSystem).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(o => o.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(o => o.RequestedTransportMode).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.RequiresDropPod).HasColumnName("RequiresDropPod");
            b.Property(o => o.RequiresPickupPod).HasColumnName("RequiresPickupPod");
            b.Property(o => o.RequestedBy).HasMaxLength(200);
            b.Property(o => o.Notes).HasMaxLength(1000);
            b.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion().IsConcurrencyToken();
            b.Ignore(o => o.DomainEvents);
            b.OwnsOne(o => o.ServiceWindow, sw =>
            {
                sw.Property(x => x.EarliestUtc).HasColumnName("ServiceWindow_EarliestUtc");
                sw.Property(x => x.LatestUtc).HasColumnName("ServiceWindow_LatestUtc");
            });
            b.Property(o => o.SubmittedAt);

            b.HasIndex(o => o.Status);
            b.HasIndex(o => o.CreatedDate);
            b.HasIndex(o => new { o.SourceSystem, o.OrderRef }).IsUnique();

            b.HasMany(o => o.Items)
             .WithOne()
             .HasForeignKey(p => p.DeliveryOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(o => o.Items)
             .HasField("_items")
             .UsePropertyAccessMode(PropertyAccessMode.Field);

        });

        modelBuilder.Entity<Item>(b =>
        {
            b.ToTable("Items", Schema);
            b.HasKey(p => p.Id);
            b.Property(p => p.PickupLocationCode).HasColumnName("PickupLocationCode").HasMaxLength(50).IsRequired();
            b.Property(p => p.DropLocationCode).HasColumnName("DropLocationCode").HasMaxLength(50).IsRequired();
            b.Property(p => p.PickupStationId);
            b.Property(p => p.DropStationId);
            // Phase 2.5 — warehouse Ids resolved by IWarehouseLookup (Phase 2.6).
            // Nullable until the lookup wiring lands; per ADR-002 every order
            // will reference a warehouse (the AMR station is optional inside it).
            b.Property(p => p.PickupWarehouseId);
            b.Property(p => p.DropWarehouseId);
            // Trip binding (Option D — item-level state derivation). Null
            // before first dispatch and between Cancel-with-retry steps.
            b.Property(p => p.TripId);
            b.Property(p => p.AttemptNumber);
            b.HasIndex(p => p.TripId).HasFilter("\"TripId\" IS NOT NULL");
            b.Property(p => p.ItemSeq).IsRequired();
            b.Property(p => p.ItemId).HasMaxLength(100).IsRequired();
            b.OwnsOne(p => p.Dimensions, d =>
            {
                d.Property(x => x.LengthMm).HasColumnName("LengthMm");
                d.Property(x => x.WidthMm).HasColumnName("WidthMm");
                d.Property(x => x.HeightMm).HasColumnName("HeightMm");
                d.Ignore(x => x.VolumeCBM);
            });
            b.HasIndex(p => new { p.DeliveryOrderId, p.ItemSeq }).IsUnique();
            b.HasIndex(p => new { p.DeliveryOrderId, p.ItemId }).IsUnique();
            b.Property(p => p.WeightKg);
            // Quantity is mapped as an owned value object. Column names are kept
            // as "Quantity" and "Uom" via HasColumnName so the existing schema
            // doesn't need a rename — only the values inside Uom get backfilled
            // to the canonical enum names by the migration.
            b.OwnsOne(p => p.Quantity, q =>
            {
                q.Property(x => x.Value).HasColumnName("Quantity").IsRequired();
                q.Property(x => x.Uom).HasConversion<string>().HasColumnName("Uom").HasMaxLength(20).IsRequired();
            });
            b.Property(p => p.LoadUnitProfileCode).HasMaxLength(50);
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.OwnsOne(p => p.Hazmat, hz =>
            {
                hz.Property(x => x.ClassCode).HasColumnName("Hazmat_ClassCode").HasMaxLength(10);
                hz.Property(x => x.PackingGroup).HasConversion<string>().HasColumnName("Hazmat_PackingGroup").HasMaxLength(5);
            });
            b.OwnsOne(p => p.Temperature, tr =>
            {
                tr.Property(x => x.MinC).HasColumnName("Temperature_MinC");
                tr.Property(x => x.MaxC).HasColumnName("Temperature_MaxC");
            });
            // HandlingInstructions stored as Postgres text[] of enum names.
            // Empty list is the default ("no special handling") and a value
            // comparer keeps EF change-tracking honest after rehydration.
            b.Property(p => p.HandlingInstructions)
                .HasConversion(
                    v => v.Select(x => x.ToString()).ToArray(),
                    v => v.Select(s => Enum.Parse<HandlingInstruction>(s)).ToArray() as IReadOnlyList<HandlingInstruction>,
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<HandlingInstruction>>(
                        (a, c) => (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c)),
                        v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                        v => v.ToArray()))
                .HasColumnType("text[]")
                .HasColumnName("HandlingInstructions")
                .IsRequired();

            // DroppedOffAt is the SLA clock anchor stamped by vendor drop
            // sub-task completion; per-checkpoint POD scans live on the
            // PodEvents child collection (one row per scan type).
            b.Property(p => p.DroppedOffAt).HasColumnName("DroppedOffAt");

            b.HasMany(p => p.PodEvents)
             .WithOne()
             .HasForeignKey(e => e.ItemId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(p => p.PodEvents)
             .HasField("_podEvents")
             .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ItemPodEvent>(b =>
        {
            b.ToTable("ItemPodEvents", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.ItemId).IsRequired();
            b.Property(e => e.ScanType).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(e => e.ScannedAt).IsRequired();
            b.Property(e => e.ScannedBy).HasMaxLength(200).IsRequired();
            b.Property(e => e.Method).HasMaxLength(20).IsRequired();
            b.Property(e => e.Reference).HasMaxLength(500);
            // At most one scan per (item, checkpoint) — duplicate scans
            // are bounced at the database, not just the aggregate.
            b.HasIndex(e => new { e.ItemId, e.ScanType }).IsUnique();
        });

        modelBuilder.Entity<OrderAmendment>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(a => a.Reason).HasMaxLength(500);
            b.Property(a => a.OriginalSnapshot).HasColumnType("jsonb");
            b.Property(a => a.NewSnapshot).HasColumnType("jsonb");
            b.Property(a => a.AmendedBy).HasMaxLength(200);
            b.Property(a => a.AmendmentVersion).IsRequired();
            b.HasIndex(a => a.DeliveryOrderId);
        });

        modelBuilder.Entity<OrderAuditEvent>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            b.Property(e => e.Details).HasMaxLength(1000);
            b.Property(e => e.ActorId).HasMaxLength(200);
            b.Property(e => e.Channel).HasMaxLength(30);
            b.Property(e => e.DisplayName).HasMaxLength(200);
            b.HasIndex(e => e.DeliveryOrderId);
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
            // mapped only on the central OutboxDbContext; this module's
            // table doesn't have the columns.
            b.Ignore(e => e.PartitionKey);
            b.Ignore(e => e.CorrelationId);
        });

        // ── Phase P1 — projection_inbox (idempotency bookkeeping) ──────
        // Owned by every projector in this module via IdempotentProjector.
        // UNIQUE(ProjectorName, EventId) is what enforces effectively-once.
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("ProjectionInbox", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.ProjectorName).HasMaxLength(200).IsRequired();
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.ProcessedAtUtc).IsRequired();
            b.HasIndex(e => new { e.ProjectorName, e.EventId }).IsUnique();
        });

        // ── Phase P1 — OrderStatusHistory read model ───────────────────
        // Written exclusively by OrderStatusHistoryProjector. Read by the
        // status-history query handler. Index supports the only two query
        // patterns: timeline-by-order, and "when did X enter status Y".
        modelBuilder.Entity<OrderStatusHistoryRow>(b =>
        {
            b.ToTable("OrderStatusHistory", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.OrderId).IsRequired();
            b.Property(e => e.FromStatus).HasMaxLength(30);
            b.Property(e => e.ToStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.OccurredAt).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(2000);
            b.HasIndex(e => new { e.OrderId, e.OccurredAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.ToStatus, e.OccurredAt });
        });

        // ── Phase P2 — OrderActivity read model ────────────────────────
        // Unified per-order activity timeline. Written by:
        //   - OrderActivityProjector (Order + Trip integration events going
        //     forward)
        //   - One-time backfill SQL seed from the 4 legacy sources
        //     (OrderAuditEvents, OrderAmendments, Trip ExecutionEvents,
        //     TripRetryEvents)
        // Read by the swapped GetFullOrderAuditQueryHandler — replaces the
        // runtime 4-source UNION with a single indexed lookup.
        modelBuilder.Entity<OrderActivityRow>(b =>
        {
            b.ToTable("OrderActivity", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.OrderId).IsRequired();
            b.Property(e => e.Category).HasMaxLength(30).IsRequired();
            b.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            b.Property(e => e.Details).HasMaxLength(2000);
            b.Property(e => e.ActorId).HasMaxLength(200);
            b.Property(e => e.OccurredAt).IsRequired();
            b.Property(e => e.RelatedTripId);
            b.Property(e => e.AttemptNumber);
            b.Property(e => e.Channel).HasMaxLength(30);
            b.Property(e => e.DisplayName).HasMaxLength(200);
            // Primary lookup — timeline-by-order, newest first.
            b.HasIndex(e => new { e.OrderId, e.OccurredAt }).IsDescending(false, true);
            // Secondary — category filter (UI chips) within an order.
            b.HasIndex(e => new { e.OrderId, e.Category, e.OccurredAt });
        });

        // ── Phase P3 — OrderFunnelHourly read model ────────────────────
        // Hour-bucketed counters incremented by OrderFunnelProjector on
        // every Order lifecycle event. Drives both the KpiRail's "today"
        // numbers and the DispatchFunnel chart. BucketHour is unique
        // (one row per hour) and is the only index needed — time-range
        // queries use it directly.
        modelBuilder.Entity<OrderFunnelHourlyRow>(b =>
        {
            b.ToTable("OrderFunnelHourly", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.BucketHour).IsRequired();
            b.HasIndex(e => e.BucketHour).IsUnique();
        });

        // ── Phase P4 — OrderListView denormalized search/list table ────
        // Owned by OrderListViewProjector. Writers MUST NOT touch the
        // row from anywhere else. EF maps the SearchText column; the
        // accompanying GENERATED tsvector + GIN index live in the
        // migration's raw-SQL section (EF Core doesn't model tsvector
        // first-class).
        modelBuilder.Entity<OrderListViewRow>(b =>
        {
            b.ToTable("OrderListView", Schema);
            b.HasKey(e => e.OrderId);
            b.Property(e => e.OrderRef).HasMaxLength(200).IsRequired();
            b.Property(e => e.Status).HasMaxLength(30).IsRequired();
            b.Property(e => e.SourceSystem).HasMaxLength(20).IsRequired();
            b.Property(e => e.Priority).HasMaxLength(20).IsRequired();
            b.Property(e => e.TransportMode).HasMaxLength(20);
            b.Property(e => e.RequestedBy).HasMaxLength(200);
            b.Property(e => e.CreatedBy).HasMaxLength(200);
            b.Property(e => e.Notes).HasMaxLength(1000);
            b.Property(e => e.LatestJobStatus).HasMaxLength(20);
            b.Property(e => e.SearchText).HasColumnType("text").IsRequired();
            // Filter indices for hot query paths
            b.HasIndex(e => new { e.Status, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.Priority, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => e.OrderRef);
            b.HasIndex(e => e.HasFailedTrip)
                .HasFilter("\"HasFailedTrip\" = true");
            b.HasIndex(e => e.HasActiveJob)
                .HasFilter("\"HasActiveJob\" = true");
        });

        // ── Phase P5 — bi.OrderFacts BI fact table ─────────────────────
        // Wide row-per-order. One column per status timestamp + dimensional
        // fields + measures. Lives in the `bi` schema (cross-cutting BI
        // prefix) but owned by the DeliveryOrder module — projector + DI
        // + migration all live here. Derived KPI columns (TimeTo... +
        // Sla...) are PostgreSQL GENERATED ALWAYS AS STORED in the
        // migration; EF only reads them.
        modelBuilder.Entity<OrderFactsRow>(b =>
        {
            b.ToTable("OrderFacts", "bi");
            b.HasKey(e => e.OrderId);
            b.Property(e => e.OrderRef).HasMaxLength(200).IsRequired();
            b.Property(e => e.SourceSystem).HasMaxLength(20).IsRequired();
            b.Property(e => e.Priority).HasMaxLength(20).IsRequired();
            b.Property(e => e.TransportMode).HasMaxLength(20);
            b.Property(e => e.RequestedBy).HasMaxLength(200);
            b.Property(e => e.FinalStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.FailureReason).HasMaxLength(2000);

            // Generated columns — EF must never write them.
            b.Property(e => e.TimeToConfirmSec)
                .HasColumnType("integer")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            b.Property(e => e.TimeToDispatchSec)
                .HasColumnType("integer")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            b.Property(e => e.TimeToCompleteSec)
                .HasColumnType("integer")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            b.Property(e => e.SlaConfirmBreached)
                .HasColumnType("boolean")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            b.Property(e => e.SlaCompleteBreached)
                .HasColumnType("boolean")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

            // Hot query paths.
            b.HasIndex(e => e.CreatedAt).IsDescending(true);
            b.HasIndex(e => new { e.Priority, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.FinalStatus, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.SourceSystem, e.CreatedAt }).IsDescending(false, true);
            // Partial index — most "SLA breach rate" queries filter to true.
            b.HasIndex(e => e.SlaConfirmBreached)
                .HasFilter("\"SlaConfirmBreached\" = true");
        });
    }
}
