using System.Net;
using System.Net.Http.Json;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #6: Outbox infrastructure.
/// Verifies: event write → row in outbox; correct Type/Content; ProcessedOnUtc set after processor runs.
/// </summary>
public class OutboxTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public OutboxTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PublishEvent_AfterWebhookCall_RowExistsInOutbox()
    {
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        // Trigger an event via webhook
        await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "finished",
            orderKey = $"OUT-{Guid.NewGuid():N}",
            upperKey = taskId.ToString()
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();

        var message = await db.OutboxMessages
            .Where(m => m.Content.Contains(taskId.ToString()))
            .FirstOrDefaultAsync();

        message.Should().NotBeNull("published event must create an outbox row");
        message!.OccurredOnUtc.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(10));
        message.Error.Should().BeNull("newly written message has no processing error yet");
    }

    [Fact]
    public async Task OutboxMessage_Type_ContainsFullyQualifiedEventName()
    {
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "failed",
            upperKey = taskId.ToString(),
            failResult = new { errorCode = "E_TEST", errorMsg = "test failure" }
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var message = await db.OutboxMessages
            .Where(m => m.Type.Contains("Riot3TaskFailedIntegrationEvent")
                     && m.Content.Contains(taskId.ToString()))
            .FirstOrDefaultAsync();

        message.Should().NotBeNull();
        // Type is the AssemblyQualifiedName — must contain namespace and assembly
        message!.Type.Should().Contain("Riot3TaskFailedIntegrationEvent");
        message.Type.Length.Should().BeGreaterThan(50,
            "AssemblyQualifiedName includes namespace + assembly info");
    }

    [Fact]
    public async Task OutboxMessage_Content_IsValidJsonWithEventData()
    {
        var client = await _factory.GetAuthenticatedClient();
        var vehicleId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 10,
                systemState = "IDLE",
                safetyState = "NORMAL"
            }
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var message = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleBatteryLowIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .FirstOrDefaultAsync();

        message.Should().NotBeNull();

        // Content must be valid JSON
        var act = () => System.Text.Json.JsonDocument.Parse(message!.Content);
        act.Should().NotThrow("outbox content must be valid JSON");

        using var doc = System.Text.Json.JsonDocument.Parse(message!.Content);
        // OutboxEventBus serializes with default JsonSerializer (PascalCase property names)
        doc.RootElement.TryGetProperty("VehicleId", out _).Should().BeTrue("event JSON must contain VehicleId");
        doc.RootElement.TryGetProperty("BatteryLevel", out _).Should().BeTrue("event JSON must contain BatteryLevel");
    }

    [Fact]
    public async Task OutboxMessage_ProcessedOnUtc_SetAfterProcessorRuns()
    {
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "task",
            taskEventType = "finished",
            upperKey = taskId.ToString(),
            orderKey = $"PROC-{Guid.NewGuid():N}"
        });

        // OutboxProcessorService polls every 5 seconds; wait up to 20 seconds for it to run
        VendorAdapterDbContext? db = null;
        AMR.DeliveryPlanning.SharedKernel.Outbox.OutboxMessage? message = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            using var scope = _factory.Services.CreateScope();
            db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
            message = await db.OutboxMessages
                .Where(m => m.Type.Contains("Riot3TaskCompletedIntegrationEvent")
                         && m.Content.Contains(taskId.ToString()))
                .FirstOrDefaultAsync();

            if (message?.ProcessedOnUtc != null) break;
        }

        message.Should().NotBeNull();
        message!.ProcessedOnUtc.Should().NotBeNull(
            "OutboxProcessorService must set ProcessedOnUtc within ~10 seconds of polling");
    }

    [Fact]
    public async Task MultipleEvents_FromSameTrigger_AllWrittenToOutbox()
    {
        var client = await _factory.GetAuthenticatedClient();
        // Vehicle event with battery < 20 % publishes TWO events:
        // VehicleStateChangedIntegrationEvent + VehicleBatteryLowIntegrationEvent
        var vehicleId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 5,
                systemState = "IDLE",
                safetyState = "NORMAL"
            }
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Content.Contains(vehicleId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(2,
            "a single vehicle webhook with low battery must write both StateChanged and BatteryLow events");
        messages.Should().Contain(m => m.Type.Contains("VehicleStateChangedIntegrationEvent"));
        messages.Should().Contain(m => m.Type.Contains("VehicleBatteryLowIntegrationEvent"));
    }
}
