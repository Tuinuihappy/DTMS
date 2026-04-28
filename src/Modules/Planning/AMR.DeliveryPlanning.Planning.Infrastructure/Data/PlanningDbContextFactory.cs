using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Data;

public class PlanningDbContextFactory : IDesignTimeDbContextFactory<PlanningDbContext>
{
    public PlanningDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PlanningDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new PlanningDbContext(options, new AMR.DeliveryPlanning.SharedKernel.Tenancy.TenantContext());
    }
}
