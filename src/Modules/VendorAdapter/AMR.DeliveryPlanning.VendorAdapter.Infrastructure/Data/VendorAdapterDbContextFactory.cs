using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;

public class VendorAdapterDbContextFactory : IDesignTimeDbContextFactory<VendorAdapterDbContext>
{
    public VendorAdapterDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<VendorAdapterDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new VendorAdapterDbContext(options);
    }
}
