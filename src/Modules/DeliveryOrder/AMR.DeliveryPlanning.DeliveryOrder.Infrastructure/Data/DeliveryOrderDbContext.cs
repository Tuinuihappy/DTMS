using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;

public class DeliveryOrderDbContext : DbContext
{
    public const string Schema = "deliveryorder";

    private readonly ITenantContext _tenantContext;

    public DbSet<Domain.Entities.DeliveryOrder> DeliveryOrders { get; set; } = null!;
    public DbSet<DeliveryLeg> DeliveryLegs { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<RecurringSchedule> RecurringSchedules { get; set; } = null!;
    public DbSet<OrderAmendment> OrderAmendments { get; set; } = null!;
    public DbSet<OrderAuditEvent> OrderAuditEvents { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DeliveryOrderDbContext(DbContextOptions<DeliveryOrderDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Domain.Entities.DeliveryOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.TenantId).IsRequired();
            b.Property(o => o.OrderId).IsRequired();
            b.Property(o => o.OrderNo).HasMaxLength(50).IsRequired();
            b.Property(o => o.CreateBy).HasMaxLength(100).IsRequired();
            b.Property(o => o.Priority).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(o => new { o.TenantId, o.OrderNo }).IsUnique();
            b.HasIndex(o => o.OrderId);
            b.HasQueryFilter(o => o.TenantId == _tenantContext.TenantId);
            // Map PostgreSQL system column xmin as optimistic concurrency token (no DDL needed — xmin is auto-maintained)
            b.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion().IsConcurrencyToken();
            b.Ignore(o => o.DomainEvents);
            b.Ignore(o => o.AllOrderItems);

            b.HasMany(o => o.Legs)
             .WithOne()
             .HasForeignKey(l => l.DeliveryOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(o => o.Schedule)
             .WithOne()
             .HasForeignKey<RecurringSchedule>(s => s.DeliveryOrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryLeg>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.PickupLocationCode).HasMaxLength(50).IsRequired();
            b.Property(l => l.DropLocationCode).HasMaxLength(50).IsRequired();

            b.HasMany(l => l.OrderItems)
             .WithOne()
             .HasForeignKey(ol => ol.DeliveryLegId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.WorkOrderId).IsRequired();
            b.Property(l => l.WorkOrder).HasMaxLength(50).IsRequired();
            b.Property(l => l.ItemId).IsRequired();
            b.Property(l => l.ItemNumber).HasMaxLength(50).IsRequired();
            b.Property(l => l.ItemDescription).HasMaxLength(200).IsRequired();
            b.Property(l => l.Line).HasMaxLength(100);
            b.Property(l => l.Model).HasMaxLength(100);
            b.Property(l => l.Remarks).HasMaxLength(200);
            b.Property(l => l.ItemStatus).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<RecurringSchedule>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.CronExpression).HasMaxLength(50).IsRequired();
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
