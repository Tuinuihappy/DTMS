using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;

public class DispatchDbContext : DbContext
{
    public const string Schema = "dispatch";

    private readonly ITenantContext _tenantContext;

    public DbSet<Trip> Trips { get; set; } = null!;
    public DbSet<RobotTask> RobotTasks { get; set; } = null!;
    public DbSet<ExecutionEvent> ExecutionEvents { get; set; } = null!;
    public DbSet<TripException> TripExceptions { get; set; } = null!;
    public DbSet<ProofOfDelivery> ProofsOfDelivery { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DispatchDbContext(DbContextOptions<DispatchDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Trip>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.TenantId).IsRequired();
            builder.HasQueryFilter(t => t.TenantId == _tenantContext.TenantId);
            builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            builder.Ignore(t => t.DomainEvents);

            builder.HasMany(t => t.Tasks)
                   .WithOne()
                   .HasForeignKey(rt => rt.TripId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(t => t.Tasks)
                   .HasField("_tasks")
                   .UsePropertyAccessMode(PropertyAccessMode.Field);

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
        });

        modelBuilder.Entity<RobotTask>(builder =>
        {
            builder.HasKey(rt => rt.Id);
            builder.Property(rt => rt.Type).HasConversion<string>().HasMaxLength(20);
            builder.Property(rt => rt.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(rt => rt.FailureReason).HasMaxLength(500);
        });

        modelBuilder.Entity<ExecutionEvent>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.EventType).HasMaxLength(50);
            builder.Property(e => e.Details).HasMaxLength(500);
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

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("OutboxMessages");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Type).HasMaxLength(500).IsRequired();
            builder.Property(e => e.Content).HasColumnType("text").IsRequired();
            builder.HasIndex(e => e.ProcessedOnUtc);
        });
    }
}
