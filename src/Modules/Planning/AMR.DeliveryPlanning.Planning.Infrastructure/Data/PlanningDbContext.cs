using AMR.DeliveryPlanning.Planning.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Data;

public class PlanningDbContext : DbContext
{
    public const string Schema = "planning";

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<Leg> Legs { get; set; } = null!;
    public DbSet<Stop> Stops { get; set; } = null!;

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

            // Store DerivedFromOrders as JSON column
            builder.Property(j => j.DerivedFromOrders)
                   .HasColumnType("jsonb");

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
    }
}
