using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMR.DeliveryPlanning.Api.Auth;

public class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new AuthDbContext(options);
    }
}
