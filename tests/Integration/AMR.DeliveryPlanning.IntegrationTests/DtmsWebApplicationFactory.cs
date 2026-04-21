using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that uses Testcontainers for PostgreSQL
/// and disables RabbitMQ (uses InMemory transport for MassTransit).
/// </summary>
public class DtmsWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("amr_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:Host", "localhost");
        builder.UseSetting("Jwt:Secret", "super-secret-key-for-testing-minimum-32-characters!");
        builder.UseSetting("Jwt:Issuer", "AMR.DeliveryPlanning");
        builder.UseSetting("Jwt:Audience", "AMR.DeliveryPlanning.Api");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
