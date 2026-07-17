using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Infrastructure.Projections;
using DTMS.SharedKernel.Outbox;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Planning.Infrastructure.Data;

public class PlanningDbContext : DbContext
{
    public const string Schema = "planning";

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<Leg> Legs { get; set; } = null!;
    public DbSet<JobDependency> JobDependencies { get; set; } = null!;
    public DbSet<ActionTemplate> ActionTemplates { get; set; } = null!;
    public DbSet<OrderTemplate> OrderTemplates { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // ── Phase P1 — Event Projection read models ──────────────────────────
    public DbSet<JobStatusHistoryRow> JobStatusHistory => Set<JobStatusHistoryRow>();
    public DbSet<InboxMessage> ProjectionInbox => Set<InboxMessage>();

    // ── Phase P5.2 — BI fact table (cross-cutting bi schema, owned here) ──
    public DbSet<JobFactsRow> JobFacts => Set<JobFactsRow>();

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
            // Phase b13 — structured failure classification. Stored as
            // the enum string so it stays human-readable in pgAdmin and
            // new enum values are additive (no integer-renumbering risk).
            builder.Property(j => j.FailureCategory)
                .HasConversion<string>()
                .HasMaxLength(40)
                .HasDefaultValue(Domain.Enums.JobFailureCategory.None);
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

        // NOTE: MilkRunTemplate/MilkRunStop mappings removed 2026-07-17 with
        // the legacy manual-planning stack (their only writer was the deleted
        // CreateMilkRun command). Tables remain in the DB — migrations are
        // handwritten, nothing will auto-drop them.

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

        // NOTE: CostModelConfigRecord mapping removed 2026-07-17 with the
        // legacy manual-planning stack. The planning."CostModelConfigs"
        // table still exists in the DB (and in historical migration
        // snapshots) — migrations here are handwritten, so nothing will
        // auto-generate a DropTable. Recreate the entity if vehicle
        // scoring ever returns.

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
            // Phase S.5 — callback route + order/trip linkage columns are
            // mapped only on the central OutboxDbContext; this module's
            // table doesn't have them.
            b.Ignore(e => e.CallbackPath);
            b.Ignore(e => e.CallbackMethod);
            b.Ignore(e => e.RelatedOrderId);
            b.Ignore(e => e.RelatedTripId);
            // Phase O4 — W3C traceparent captured at write time.
            b.Property(e => e.TraceParent).HasMaxLength(55);
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

        // ── Phase P5.2 — bi.JobFacts BI fact table ─────────────────────
        modelBuilder.Entity<JobFactsRow>(b =>
        {
            b.ToTable("JobFacts", "bi");
            b.HasKey(e => e.JobId);
            b.Property(e => e.VendorOrderKey).HasMaxLength(100);
            b.Property(e => e.FinalStatus).HasMaxLength(30).IsRequired();
            b.Property(e => e.FailureReason).HasMaxLength(2000);
            // Phase #9 — structured failure classification (b13 → BI side).
            b.Property(e => e.FailureCategory)
                .HasMaxLength(40)
                .HasDefaultValue("None")
                .IsRequired();

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
            b.Property(e => e.SlaDispatchBreached)
                .HasColumnType("boolean")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(
                    Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

            b.HasIndex(e => e.CreatedAt).IsDescending(true);
            b.HasIndex(e => new { e.FinalStatus, e.CreatedAt }).IsDescending(false, true);
            b.HasIndex(e => e.DeliveryOrderId);
            b.HasIndex(e => new { e.AttemptNumber, e.CreatedAt })
                .HasFilter("\"AttemptNumber\" > 1");
            b.HasIndex(e => e.SlaDispatchBreached)
                .HasFilter("\"SlaDispatchBreached\" = true");
            // Hot path: Job failures report groups by FailureCategory.
            // Filter partial — most rows are FinalStatus="Completed" with
            // category "None"; only the failure tail matters.
            b.HasIndex(e => new { e.FailureCategory, e.CreatedAt })
                .HasFilter("\"FailureCategory\" <> 'None'");
        });
    }
}
