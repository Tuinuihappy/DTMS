using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;

public class DeliveryOrderDbContext : DbContext
{
    public const string Schema = "deliveryorder";

    public DbSet<Domain.Entities.DeliveryOrder> DeliveryOrders { get; set; } = null!;
    public DbSet<OrderLine> OrderLines { get; set; } = null!;
    public DbSet<RecurringSchedule> RecurringSchedules { get; set; } = null!;
    public DbSet<OrderAmendment> OrderAmendments { get; set; } = null!;
    public DbSet<OrderAuditEvent> OrderAuditEvents { get; set; } = null!;

    public DeliveryOrderDbContext(DbContextOptions<DeliveryOrderDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Domain.Entities.DeliveryOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.OrderKey).HasMaxLength(50).IsRequired();
            b.Property(o => o.PickupLocationCode).HasMaxLength(50).IsRequired();
            b.Property(o => o.DropLocationCode).HasMaxLength(50).IsRequired();
            b.Property(o => o.Priority).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(o => o.OrderKey).IsUnique();

            b.HasMany(o => o.OrderLines)
             .WithOne()
             .HasForeignKey(l => l.DeliveryOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(o => o.Schedule)
             .WithOne()
             .HasForeignKey<RecurringSchedule>(s => s.DeliveryOrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.ItemCode).HasMaxLength(50).IsRequired();
            b.Property(l => l.Remarks).HasMaxLength(200);
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
    }
}
