using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DTMS.Wms.Infrastructure.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> tooling can instantiate the
/// context outside the app host. Mirrors FacilityDbContextFactory —
/// falls back to the local docker-compose Postgres if no env var is set
/// so IDEs can scaffold migrations without extra config.
/// </summary>
public class WmsDbContextFactory : IDesignTimeDbContextFactory<WmsDbContext>
{
    public WmsDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new WmsDbContext(options);
    }
}
