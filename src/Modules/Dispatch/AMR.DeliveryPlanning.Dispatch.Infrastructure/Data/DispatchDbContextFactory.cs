using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;

public class DispatchDbContextFactory : IDesignTimeDbContextFactory<DispatchDbContext>
{
    public DispatchDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DispatchDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new DispatchDbContext(options, new AMR.DeliveryPlanning.SharedKernel.Tenancy.TenantContext());
    }
}
