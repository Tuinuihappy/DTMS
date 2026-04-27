using AMR.DeliveryPlanning.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

public class OutboxDbContext : DbContext
{
    public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("outbox");

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasMaxLength(500).IsRequired();
            e.Property(m => m.Content).IsRequired();
            e.Property(m => m.OccurredOnUtc).IsRequired();
            e.HasIndex(m => m.ProcessedOnUtc);
        });
    }
}
