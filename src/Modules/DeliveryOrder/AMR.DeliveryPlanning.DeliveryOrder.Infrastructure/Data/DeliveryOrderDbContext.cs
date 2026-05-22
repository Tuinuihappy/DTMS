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
            b.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion().IsConcurrencyToken();
            b.Ignore(o => o.DomainEvents);
            b.Property(o => o.RequestedDeliveryDate);

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
            b.Property(p => p.PickupLocationCode).HasMaxLength(50).IsRequired();
            b.Property(p => p.DropLocationCode).HasMaxLength(50).IsRequired();
            b.Property(p => p.PickupStationId);
            b.Property(p => p.DropStationId);
            b.Property(p => p.ItemSeq).IsRequired();
            b.Property(p => p.Sku).HasMaxLength(100).IsRequired();
            b.Property(p => p.CargoType).HasConversion<string>().HasMaxLength(30);
            b.OwnsOne(p => p.Dimensions, d =>
            {
                d.Property(x => x.LengthMm).HasColumnName("LengthMm");
                d.Property(x => x.WidthMm).HasColumnName("WidthMm");
                d.Property(x => x.HeightMm).HasColumnName("HeightMm");
                d.Ignore(x => x.VolumeCBM);
            });
            b.HasIndex(p => new { p.DeliveryOrderId, p.ItemSeq }).IsUnique();
            b.HasIndex(p => p.Sku);
            b.Property(p => p.WeightKg);
            b.Property(p => p.Quantity).IsRequired();
            b.Property(p => p.Uom).HasMaxLength(20).IsRequired();
            b.Property(p => p.LoadUnitProfileCode).HasMaxLength(50);
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.OwnsOne(p => p.CargoSpecific, cs =>
            {
                cs.Property(x => x.PartNo).HasColumnName("PartNo").HasMaxLength(100);
                cs.Property(x => x.Wo).HasColumnName("Wo").HasMaxLength(100);
                cs.Property(x => x.Line).HasColumnName("Line").HasMaxLength(100);
                cs.Property(x => x.Vendor).HasColumnName("Vendor").HasMaxLength(200);
                cs.Property(x => x.DateCode).HasColumnName("DateCode").HasMaxLength(50);
                cs.Property(x => x.TradingCode).HasColumnName("TradingCode").HasMaxLength(100);
                cs.Property(x => x.InventoryNo).HasColumnName("InventoryNo").HasMaxLength(100);
                cs.Property(x => x.Po).HasColumnName("Po").HasMaxLength(100);
                cs.Property(x => x.TraceId).HasColumnName("TraceId").HasMaxLength(100);
                cs.Property(x => x.LotNo).HasColumnName("LotNo").HasMaxLength(100);
            });
        });

        modelBuilder.Entity<OrderAmendment>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(a => a.Reason).HasMaxLength(500);
            b.Property(a => a.OriginalSnapshot).HasColumnType("jsonb");
            b.Property(a => a.NewSnapshot).HasColumnType("jsonb");
            b.Property(a => a.AmendedBy).HasMaxLength(200);
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
            b.HasIndex(e => e.ProcessedOnUtc);
        });
    }
}
