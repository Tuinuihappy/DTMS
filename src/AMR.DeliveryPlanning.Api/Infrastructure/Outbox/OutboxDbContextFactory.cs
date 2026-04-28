using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

public class OutboxDbContextFactory : IDesignTimeDbContextFactory<OutboxDbContext>
{
    public OutboxDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new OutboxDbContext(options);
    }
}
