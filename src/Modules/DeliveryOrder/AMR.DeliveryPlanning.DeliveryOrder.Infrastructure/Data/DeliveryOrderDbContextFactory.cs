using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;

public class DeliveryOrderDbContextFactory : IDesignTimeDbContextFactory<DeliveryOrderDbContext>
{
    public DeliveryOrderDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DeliveryOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new DeliveryOrderDbContext(options);
    }
}
