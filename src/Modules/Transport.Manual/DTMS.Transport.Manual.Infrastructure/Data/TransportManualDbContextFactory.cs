using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Transport.Manual.Infrastructure.Data;

// Design-time factory — only used by `dotnet ef` tooling. Production
// runtime uses ModuleServiceRegistration.AddDbContext<TransportManualDbContext>.
// Per feedback_migration_manual.md memory: dotnet-ef is incompatible with
// .NET 10 preview, so we ship hand-written migrations. This factory exists
// for the rare case we want to scaffold a starting point off the model
// snapshot via a separate temporary .NET 8 SDK pin.
public class TransportManualDbContextFactory : IDesignTimeDbContextFactory<TransportManualDbContext>
{
    public TransportManualDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<TransportManualDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new TransportManualDbContext(options);
    }
}
