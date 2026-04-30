using System.Net;
using System.Net.Http.Json;
using AMR.DeliveryPlanning.Api.Infrastructure.Outbox;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #2: RIOT3 webhook notify endpoint.
/// Verifies event routing: finished → Riot3TaskCompletedIntegrationEvent,
/// failed → Riot3TaskFailedIntegrationEvent, unknown → safe 200 log.
/// </summary>
public class Riot3WebhookTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public Riot3WebhookTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Notify_TaskFinished_WritesCompletedEventToOutbox()
    {
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "finished",
            orderKey = $"ORD-{Guid.NewGuid():N}",
            upperKey = taskId.ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("Riot3TaskCompletedIntegrationEvent")
                     && m.Content.Contains(taskId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1, "finished webhook must produce exactly one completed event");
        messages[0].ProcessedOnUtc.Should().BeNull("event should not be processed yet");
        messages[0].Error.Should().BeNull();
    }

    [Fact]
    public async Task Notify_TaskFailed_WritesFailedEventToOutbox()
    {
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();
        var errorCode = "E_OBSTACLE";

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "failed",
            orderKey = $"ORD-{Guid.NewGuid():N}",
            upperKey = taskId.ToString(),
            failResult = new { errorCode, errorMsg = "Obstacle detected" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("Riot3TaskFailedIntegrationEvent")
                     && m.Content.Contains(taskId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1, "failed webhook must produce exactly one failed event");
        messages[0].Content.Should().Contain(errorCode, "error code must be stored in event content");
    }

    [Fact]
    public async Task Notify_UnknownTaskEventType_Returns200WithoutError()
    {
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "unexpected_event_type",
            upperKey = taskId.ToString()
        });

        // Handler logs warning but does not throw
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // No event written to outbox for unrecognised task event types
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var count = await db.OutboxMessages
            .CountAsync(m => m.Content.Contains(taskId.ToString()));
        count.Should().Be(0, "unknown task event type must not produce any outbox event");
    }

    [Fact]
    public async Task Notify_UnknownTopLevelType_Returns200Safely()
    {
        var client = await _factory.GetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "unknown_device_type",
            taskEventType = "finished",
            upperKey = Guid.NewGuid().ToString()
        });

        // Handler logs warning and falls through — must never return 5xx
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Notify_TaskFinishedWithInvalidUpperKey_Returns200WithoutEvent()
    {
        var client = await _factory.GetAuthenticatedClient();

        // upperKey is not a valid Guid — handler skips processing
        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "finished",
            upperKey = "not-a-guid"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "webhook must gracefully handle invalid upperKey without 5xx");
    }

    [Fact]
    public async Task Notify_VehicleEventWithVendorDeviceKey_MapsToAppVehicleId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var vehicleTypeId = await _factory.CreateVehicleTypeAsync();
        var deviceKey = $"SEER-{Guid.NewGuid():N}"[..20];

        var regResp = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = $"RIOT3-{Guid.NewGuid():N}"[..20],
            VehicleTypeId = vehicleTypeId,
            AdapterKey = "riot3",
            VendorVehicleKey = deviceKey
        });
        regResp.IsSuccessStatusCode.Should().BeTrue(
            $"Vehicle registration failed: {await regResp.Content.ReadAsStringAsync()}");
        var vehicleId = await regResp.Content.ReadFromJsonAsync<Guid>();

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey,
                batteryLevel = 80,
                systemState = "IDLE",
                safetyState = "NORMAL"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleStateChangedIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1,
            "RIOT3 vehicle deviceKey must be resolved to the app VehicleId before publishing state events");
    }
}
