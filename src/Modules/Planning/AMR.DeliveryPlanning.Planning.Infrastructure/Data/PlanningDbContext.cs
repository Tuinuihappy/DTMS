using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data.Records;
using AMR.DeliveryPlanning.Planning.Infrastructure.Projections;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Data;

public class PlanningDbContext : DbContext
{
    public const string Schema = "planning";

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<Leg> Legs { get; set; } = null!;
    public DbSet<JobDependency> JobDependencies { get; set; } = null!;
    public DbSet<MilkRunTemplate> MilkRunTemplates { get; set; } = null!;
    public DbSet<MilkRunStop> MilkRunStops { get; set; } = null!;
    public DbSet<ActionTemplate> ActionTemplates { get; set; } = null!;
    public DbSet<OrderTemplate> OrderTemplates { get; set; } = null!;
    public DbSet<CostModelConfigRecord> CostModelConfigs { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // ── Phase P1 — Event Projection read models ──────────────────────────
    public DbSet<JobStatusHistoryRow> JobStatusHistory => Set<JobStatusHistoryRow>();
    public DbSet<InboxMessage> ProjectionInbox => Set<InboxMessage>();

    public PlanningDbContext(DbContextOptions<PlanningDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Job>(builder =>
        {
            builder.HasKey(j => j.Id);
            builder.Property(j => j.Priority).HasMaxLength(20);
            builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(j => j.Pattern).HasConversion<string>().HasMaxLength(30);
            builder.Property(j => j.RequiredCapability).HasMaxLength(50);
            builder.Property(j => j.TotalWeight);
            builder.Property(j => j.SlaDeadline);
            builder.Property(j => j.TransportMode).HasMaxLength(20);
            builder.Property(j => j.PlanningTrace).HasColumnType("text");
            // Phase b8 — envelope-dispatch anchor fields.
            builder.Property(j => j.TripId);
            builder.Property(j => j.VendorOrderKey).HasMaxLength(100);
            builder.Property(j => j.FailureReason).HasMaxLength(1000);
            builder.Property(j => j.AttemptNumber).HasDefaultValue(1);
            builder.Property(j => j.GroupIndex).HasDefaultValue(1);
            builder.Property(j => j.PickupStationId);
            builder.Property(j => j.DropStationId);
            // Reverse lookup Trip → Job (rare, but useful for compliance).
            builder.HasIndex(j => j.TripId).HasFilter("\"TripId\" IS NOT NULL");
            builder.Ignore(j => j.DomainEvents);

            builder.Property(j => j.DerivedFromOrders)
                   .HasColumnType("jsonb");

            builder.Property(j => j.PackageBarcodes)
                   .HasConversion(
                       v => string.Join(',', v),
                       v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                   .HasColumnType("text")
                   .HasField("_packageBarcodes")
                   .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.HasMany(j => j.Legs)
                   .WithOne()
                   .HasForeignKey(l => l.JobId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Leg>(builder =>
        {
            builder.HasKey(l => l.Id);
        });

        modelBuilder.Entity<JobDependency>(builder =>
        {
            builder.HasKey(d => d.Id);
            builder.Property(d => d.DependencyType).HasMaxLength(30);
            builder.HasIndex(d => d.PredecessorJobId);
            builder.HasIndex(d => d.SuccessorJobId);
        });

        modelBuilder.Entity<MilkRunTemplate>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.TemplateName).HasMaxLength(100);
            builder.Property(t => t.CronSchedule).HasMaxLength(50);

            builder.HasMany(t => t.Stops)
                   .WithOne()
                   .HasForeignKey(s => s.TemplateId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MilkRunStop>(builder =>
        {
            builder.HasKey(s => s.Id);
        });

        modelBuilder.Entity<ActionTemplate>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
            // Case-insensitive uniqueness via a lower(name) functional index.
            // Plain unique index would let "Lift" and "lift" coexist.
            builder.HasIndex(t => t.Name).IsUnique().HasDatabaseName("IX_ActionTemplates_Name_Unique");
            // DTMS-local category — STD/ACT. Stored as the uppercase token
            // for human-readable rows + easy filtering.
            builder.Property(t => t.ActionCategory)
                .HasConversion(v => v.ToString().ToUpperInvariant(),
                               v => (ActionCategory)Enum.Parse(typeof(ActionCategory), v, true))
                .HasMaxLength(50)
                .IsRequired();
            builder.Property(t => t.VendorActionId).IsRequired();
            // RIOT3 wire actionType (e.g. "standardRobotsCustom"). Column
            // name matches the field name on the wire so the entity, DB and
            // RIOT3 payload all line up.
            builder.Property(t => t.ActionType)
                .HasMaxLength(50)
                .HasDefaultValue(ActionTemplate.DefaultActionType)
                .IsRequired();
            builder.Property(t => t.Param0).IsRequired();
            builder.Property(t => t.Param1).IsRequired();
            builder.Property(t => t.ParamStr).HasMaxLength(500);
            builder.Property(t => t.CreatedBy).HasMaxLength(100);
            builder.Property(t => t.ModifiedBy).HasMaxLength(100);
            builder.Property(t => t.IsActive).HasDefaultValue(true).IsRequired();
            builder.HasIndex(t => t.IsActive);
            builder.Ignore(t => t.DomainEvents);
        });

        modelBuilder.Entity<OrderTemplate>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
            builder.HasIndex(t => t.Name).IsUnique().HasDatabaseName("IX_OrderTemplates_Name_Unique");
            builder.Property(t => t.Priority).IsRequired();
            builder.Property(t => t.StructureType).HasMaxLength(20).IsRequired();
            builder.Property(t => t.TransportOrderPriority).IsRequired();
            builder.Property(t => t.AppointVehicleKey).HasMaxLength(200);
            builder.Property(t => t.AppointVehicleName).HasMaxLength(200);
            builder.Property(t => t.AppointVehicleGroupKey).HasMaxLength(500);
            builder.Property(t => t.AppointVehicleGroupName).HasMaxLength(500);
            builder.Property(t => t.AppointQueueWaitArea).HasMaxLength(200);
            builder.Property(t => t.Description).HasMaxLength(500);
            builder.Property(t => t.CreatedBy).HasMaxLength(100);
            builder.Property(t => t.ModifiedBy).HasMaxLength(100);
            builder.Property(t => t.IsActive).HasDefaultValue(true).IsRequired();
            builder.HasIndex(t => t.IsActive);

            builder.Property(t => t.PickupStationId);
            builder.Property(t => t.DropStationId);
            // Composite index for route-based template lookup from
            // DeliveryOrderValidatedConsumer when an order is confirmed.
            builder.HasIndex(t => new { t.PickupStationId, t.DropStationId, t.IsActive });

            // Missions are owned solely by the parent template; storing them as
            // a single jsonb document keeps reads cheap and matches the RIOT3
            // payload shape we hydrate from / serialize to. The converter
            // round-trips the entire list at once.
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters =
                {
                    new System.Text.Json.Serialization.JsonStringEnumConverter(
                        System.Text.Json.JsonNamingPolicy.CamelCase)
                }
            };
            builder.Property(t => t.Missions)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, jsonOptions),
                    v => string.IsNullOrEmpty(v)
                        ? new List<OrderTemplateMission>()
                        : (IReadOnlyList<OrderTemplateMission>)System.Text.Json.JsonSerializer
                            .Deserialize<List<OrderTemplateMission>>(v, jsonOptions)!)
                .HasColumnType("jsonb")
                .IsRequired();

            builder.Ignore(t => t.DomainEvents);
        });

        modelBuilder.Entity<CostModelConfigRecord>(b =>
        {
            b.HasKey(c => c.Id);
            // null VehicleTypeKey = the global default config
            b.HasIndex(c => c.VehicleTypeKey).IsUnique().HasFilter("\"VehicleTypeKey\" IS NOT NULL");
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

        // ── Phase P1 — projection_inbox (idempotency bookkeeping) ──────
        // Same shape as the DeliveryOrder module's inbox; required by every
        // projector that lives in the Planning module.
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("ProjectionInbox", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.ProjectorName).HasMaxLength(200).IsRequired();
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.ProcessedAtUtc).IsRequired();
            b.HasIndex(e => new { e.ProjectorName, e.EventId }).IsUnique();
        });

        // ── Phase P1 — JobStatusHistory read model ─────────────────────
        // Written exclusively by JobStatusHistoryProjector. DeliveryOrderId
        // is denormalized onto every row so a single index can power both
        // per-job and per-order timeline queries (the latter unions across
        // multi-group orders).
        modelBuilder.Entity<JobStatusHistoryRow>(b =>
        {
            b.ToTable("JobStatusHistory", Schema);
            b.HasKey(e => e.Id);
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.JobId).IsRequired();
            b.Property(e => e.DeliveryOrderId).IsRequired();
            b.Property(e => e.FromStatus).HasMaxLength(30);
            b.Property(e => e.ToStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.OccurredAt).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(2000);
            b.HasIndex(e => new { e.JobId, e.OccurredAt }).IsDescending(false, true);
            b.HasIndex(e => new { e.DeliveryOrderId, e.OccurredAt });
            b.HasIndex(e => new { e.ToStatus, e.OccurredAt });
        });
    }
}
