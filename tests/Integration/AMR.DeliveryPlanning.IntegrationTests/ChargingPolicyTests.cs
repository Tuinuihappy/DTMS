using System.Net;
using System.Net.Http.Json;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #5: Charging policy and battery-low event.
/// RIOT3 vehicle webhook with battery &lt; 20 % → VehicleBatteryLowIntegrationEvent in outbox.
/// Battery ≥ 20 % → no battery-low event.
/// </summary>
public class ChargingPolicyTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public ChargingPolicyTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task VehicleWebhook_BatteryBelowThreshold_WritesBatteryLowEventToOutbox()
    {
        var client = await _factory.GetAuthenticatedClient();
        var vehicleTypeId = await _factory.CreateVehicleTypeAsync();

        // Create charging policy (low threshold 20 %)
        var policyResp = await client.PostAsJsonAsync("/api/fleet/charging-policies", new
        {
            VehicleTypeId = vehicleTypeId,
            LowThresholdPct = 0.20,
            TargetThresholdPct = 0.80,
            Mode = 0 // Opportunistic
        });
        policyResp.IsSuccessStatusCode.Should().BeTrue(
            $"Upsert charging policy failed: {await policyResp.Content.ReadAsStringAsync()}");

        // RIOT3 vehicle event: battery = 15 (< 20 %)
        var vehicleId = Guid.NewGuid();
        var webhookResp = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 15,       // 15 % — below 20 % threshold
                systemState = "IDLE",
                safetyState = "NORMAL"
            }
        });
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // VehicleBatteryLowIntegrationEvent must appear in outbox
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleBatteryLowIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1,
            "battery below 20 % must publish exactly one VehicleBatteryLowIntegrationEvent");
        messages[0].Content.Should().Contain("0.15", "battery percentage must be stored as 0.15");
    }

    [Fact]
    public async Task VehicleWebhook_BatteryAboveThreshold_DoesNotWriteBatteryLowEvent()
    {
        var client = await _factory.GetAuthenticatedClient();

        // Battery = 50 % — well above the 20 % threshold
        var vehicleId = Guid.NewGuid();
        var webhookResp = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 50,
                systemState = "BUSY",
                safetyState = "NORMAL"
            }
        });
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // No VehicleBatteryLowIntegrationEvent expected
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var count = await db.OutboxMessages
            .CountAsync(m => m.Type.Contains("VehicleBatteryLowIntegrationEvent")
                          && m.Content.Contains(vehicleId.ToString()));

        count.Should().Be(0, "battery above threshold must not produce battery-low event");
    }

    [Fact]
    public async Task VehicleWebhook_BatteryExactly19_WritesBatteryLowEvent()
    {
        var client = await _factory.GetAuthenticatedClient();
        var vehicleId = Guid.NewGuid();

        // 19 % is still < 20 % (< 0.20 check is strict less-than)
        var webhookResp = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 19,
                systemState = "IDLE",
                safetyState = "NORMAL"
            }
        });
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleBatteryLowIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1, "19 % battery is strictly below 20 % threshold");
    }

    [Fact]
    public async Task VehicleWebhook_AlsoWritesStateChangedEvent()
    {
        var client = await _factory.GetAuthenticatedClient();
        var vehicleId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 80,
                systemState = "BUSY",
                safetyState = "NORMAL"
            }
        });

        // VehicleStateChangedIntegrationEvent is always published for vehicle events
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleStateChangedIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1,
            "vehicle webhook must always publish VehicleStateChangedIntegrationEvent");
    }
}
