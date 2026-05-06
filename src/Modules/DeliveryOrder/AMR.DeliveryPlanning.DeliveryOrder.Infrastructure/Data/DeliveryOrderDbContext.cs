using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
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
    public DbSet<PackageUnit> PackageUnits { get; set; } = null!;
    public DbSet<PackageContent> PackageContents { get; set; } = null!;
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
            b.Property(o => o.OrderName).HasMaxLength(200).IsRequired();
            b.Property(o => o.SlaTier).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(o => o.StructureType).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(o => o.Tags).HasColumnType("text[]");
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            b.HasQueryFilter(o => o.TenantId == _tenantContext.TenantId);
            b.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion().IsConcurrencyToken();
            b.Ignore(o => o.DomainEvents);
            b.Ignore(o => o.AllPackages);

            b.OwnsOne(o => o.ServiceWindow, sw =>
            {
                sw.Property(s => s.Earliest).HasColumnName("ServiceWindowEarliest");
                sw.Property(s => s.Latest).HasColumnName("ServiceWindowLatest");
            });

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
            b.Property(l => l.CarrierTypeCode).HasMaxLength(50).IsRequired();
            b.HasIndex(l => l.DeliveryOrderId);

            b.HasMany(l => l.Packages)
             .WithOne()
             .HasForeignKey(p => p.DeliveryLegId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(l => l.Packages)
             .HasField("_packages")
             .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PackageUnit>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Barcode).HasMaxLength(100).IsRequired();
            b.HasIndex(p => p.Barcode).IsUnique();
            b.HasIndex(p => p.DeliveryLegId);
            b.Property(p => p.LoadUnitProfileCode).HasMaxLength(50).IsRequired();
            b.Property(p => p.GrossWeightKg).IsRequired();
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            b.HasMany(p => p.Contents)
             .WithOne()
             .HasForeignKey(c => c.PackageUnitId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(p => p.Contents)
             .HasField("_contents")
             .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PackageContent>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.ItemNumber).HasMaxLength(100).IsRequired();
            b.Property(c => c.Quantity).IsRequired();
            b.HasIndex(c => c.PackageUnitId);
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
