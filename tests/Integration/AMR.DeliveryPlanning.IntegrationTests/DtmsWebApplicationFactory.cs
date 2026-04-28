using System.Net.Http.Json;
using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory backed by a real PostgreSQL Testcontainer.
/// Redis is replaced with an in-memory distributed cache so tests run
/// without a Redis instance. MassTransit uses OutboxEventBus, so event
/// publishing is DB-backed and does not require RabbitMQ for most scenarios.
/// </summary>
public class DtmsWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("amr_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:Host", "localhost");
        builder.UseSetting("Jwt:Secret", "super-secret-key-for-testing-minimum-32-characters!");
        builder.UseSetting("Jwt:Issuer", "AMR.DeliveryPlanning");
        builder.UseSetting("Jwt:Audience", "AMR.DeliveryPlanning.Api");

        builder.ConfigureServices(services =>
        {
            // Replace Redis distributed cache with in-memory so tests need no Redis instance.
            // Route cost cache (15s TTL) still works — just not shared across processes.
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task<(Guid PickupId, Guid DropId)> CreateStationPairAsync(HttpClient client)
    {
        var mapResponse = await client.PostAsJsonAsync("/api/facility/maps", new
        {
            Name = $"TestMap-{Guid.NewGuid():N}",
            Version = "1.0",
            Width = 100.0,
            Height = 100.0,
            MapData = "{\"floor\":1}"
        });
        await EnsureSuccessAsync(mapResponse, "Create map failed");

        var mapId = await mapResponse.Content.ReadFromJsonAsync<Guid>();

        var pickupResponse = await client.PostAsJsonAsync($"/api/facility/maps/{mapId}/stations", new
        {
            Name = $"Pickup-{Guid.NewGuid():N}",
            X = 10.0,
            Y = 10.0,
            Theta = 0.0,
            Type = 2
        });
        await EnsureSuccessAsync(pickupResponse, "Create pickup station failed");
        var pickupId = await pickupResponse.Content.ReadFromJsonAsync<Guid>();

        var dropResponse = await client.PostAsJsonAsync($"/api/facility/maps/{mapId}/stations", new
        {
            Name = $"Drop-{Guid.NewGuid():N}",
            X = 80.0,
            Y = 80.0,
            Theta = 0.0,
            Type = 3
        });
        await EnsureSuccessAsync(dropResponse, "Create drop station failed");
        var dropId = await dropResponse.Content.ReadFromJsonAsync<Guid>();

        return (pickupId, dropId);
    }

    /// <summary>
    /// Inserts a VehicleType directly via EF (no HTTP endpoint exists for this) and returns its Id.
    /// Required before registering vehicles, since RegisterVehicleCommandHandler validates the type exists.
    /// </summary>
    public async Task<Guid> CreateVehicleTypeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var vehicleType = new VehicleType(Guid.NewGuid(), "TestAMR", 100.0, new[] { "MOVE" });
        db.VehicleTypes.Add(vehicleType);
        await db.SaveChangesAsync();
        return vehicleType.Id;
    }

    /// <summary>Builds a minimal single-leg Legs list for DispatchTripCommand.</summary>
    public static List<object> BuildSingleLeg(Guid pickupStationId, Guid dropStationId) =>
        new()
        {
            new { FromStationId = pickupStationId, ToStationId = dropStationId, SequenceOrder = 1 }
        };

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync();

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string message)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{message}: {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }
}
