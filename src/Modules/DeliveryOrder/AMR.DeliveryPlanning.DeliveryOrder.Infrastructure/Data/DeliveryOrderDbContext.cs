using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;

public class DeliveryOrderDbContext : DbContext
{
    public const string Schema = "deliveryorder";

    public DbSet<Domain.Entities.DeliveryOrder> DeliveryOrders { get; set; } = null!;
    public DbSet<OrderLine> OrderLines { get; set; } = null!;
    public DbSet<RecurringSchedule> RecurringSchedules { get; set; } = null!;

    public DeliveryOrderDbContext(DbContextOptions<DeliveryOrderDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Domain.Entities.DeliveryOrder>(builder =>
        {
            builder.HasKey(o => o.Id);
            builder.Property(o => o.OrderKey).HasMaxLength(50).IsRequired();
            builder.Property(o => o.PickupLocationCode).HasMaxLength(50).IsRequired();
            builder.Property(o => o.DropLocationCode).HasMaxLength(50).IsRequired();
            
            // Map Priority and Status as strings
            builder.Property(o => o.Priority).HasConversion<string>().HasMaxLength(20);
            builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

            // One-to-Many relationship with OrderLine
            builder.HasMany(o => o.OrderLines)
                   .WithOne()
                   .HasForeignKey(l => l.DeliveryOrderId)
                   .OnDelete(DeleteBehavior.Cascade);

            // One-to-One relationship with RecurringSchedule
            builder.HasOne(o => o.Schedule)
                   .WithOne()
                   .HasForeignKey<RecurringSchedule>(s => s.DeliveryOrderId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(builder =>
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.ItemCode).HasMaxLength(50).IsRequired();
            builder.Property(l => l.Remarks).HasMaxLength(200);
        });

        modelBuilder.Entity<RecurringSchedule>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.CronExpression).HasMaxLength(50).IsRequired();
        });
    }
}
