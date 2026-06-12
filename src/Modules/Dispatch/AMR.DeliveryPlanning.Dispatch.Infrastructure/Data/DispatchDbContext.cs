using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;

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

    public DispatchDbContext(DbContextOptions<DispatchDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Trip>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(t => t.UpperKey).HasMaxLength(80).IsRequired();
            builder.Property(t => t.VendorOrderKey).HasMaxLength(100);
            builder.Property(t => t.VendorVehicleKey).HasMaxLength(100);
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
        });
    }
}
