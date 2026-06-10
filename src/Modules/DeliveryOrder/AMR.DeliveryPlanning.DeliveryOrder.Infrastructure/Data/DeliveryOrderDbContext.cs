using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;

public class DeliveryOrderDbContext : DbContext
{
    public const string Schema = "deliveryorder";

    public DbSet<Domain.Entities.DeliveryOrder> DeliveryOrders { get; set; } = null!;
    public DbSet<Item> Items { get; set; } = null!;
    public DbSet<ItemPodEvent> ItemPodEvents { get; set; } = null!;
    public DbSet<OrderAmendment> OrderAmendments { get; set; } = null!;
    public DbSet<OrderAuditEvent> OrderAuditEvents { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

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
        });
    }
}
