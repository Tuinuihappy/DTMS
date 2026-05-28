using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data.Records;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Data;

public class PlanningDbContext : DbContext
{
    public const string Schema = "planning";

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<Leg> Legs { get; set; } = null!;
    public DbSet<Stop> Stops { get; set; } = null!;
    public DbSet<JobDependency> JobDependencies { get; set; } = null!;
    public DbSet<MilkRunTemplate> MilkRunTemplates { get; set; } = null!;
    public DbSet<MilkRunStop> MilkRunStops { get; set; } = null!;
    public DbSet<ActionTemplate> ActionTemplates { get; set; } = null!;
    public DbSet<OrderTemplate> OrderTemplates { get; set; } = null!;
    public DbSet<CostModelConfigRecord> CostModelConfigs { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

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
            builder.Property(j => j.PlanningTrace).HasColumnType("text");
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

            builder.HasMany(l => l.Stops)
                   .WithOne()
                   .HasForeignKey(s => s.LegId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Stop>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(20);
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
            builder.Property(t => t.ActionType).HasMaxLength(50).IsRequired();
            builder.Property(t => t.VendorActionId).IsRequired();
            builder.Property(t => t.Param0).IsRequired();
            builder.Property(t => t.Param1).IsRequired();
            builder.Property(t => t.ParamStr).HasMaxLength(500);
            builder.Property(t => t.Description).HasMaxLength(500);
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
            builder.Property(t => t.IsActive).HasDefaultValue(true).IsRequired();
            builder.HasIndex(t => t.IsActive);

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
    }
}
