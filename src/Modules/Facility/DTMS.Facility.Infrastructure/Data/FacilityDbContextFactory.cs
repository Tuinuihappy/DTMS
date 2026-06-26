using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Data;

public class FacilityDbContextFactory : IDesignTimeDbContextFactory<FacilityDbContext>
{
    public FacilityDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new FacilityDbContext(options);
    }
}
