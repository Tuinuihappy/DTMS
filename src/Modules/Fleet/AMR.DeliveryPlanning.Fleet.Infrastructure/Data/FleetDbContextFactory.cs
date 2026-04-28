using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Data;

public class FleetDbContextFactory : IDesignTimeDbContextFactory<FleetDbContext>
{
    public FleetDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new FleetDbContext(options, new AMR.DeliveryPlanning.SharedKernel.Tenancy.TenantContext());
    }
}
