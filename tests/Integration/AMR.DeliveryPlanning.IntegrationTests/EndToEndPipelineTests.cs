using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #1: Full pipeline Submit → Plan → Assign → Commit → Dispatch → Complete.
/// Uses Option B: each step called manually via HTTP (no in-process MassTransit consumers).
/// </summary>
public class EndToEndPipelineTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public EndToEndPipelineTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Pipeline_FullFlow_TripReachesCompletedStatus()
    {
        var client = await _factory.GetAuthenticatedClient();

        // Setup
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var vehicleTypeId = await _factory.CreateVehicleTypeAsync();
        var vehicleId = await RegisterAndSetIdleAsync(client, vehicleTypeId);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        // 1. Submit order
        var orderId = await SubmitOrderAsync(client, pickupId, dropId, profileCode);

        // 2. Create planning job for that order
        var jobResp = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = orderId,
            PickupStationId = pickupId,
            DropStationId = dropId,
            Priority = "Normal"
        });
        jobResp.IsSuccessStatusCode.Should().BeTrue($"Create job failed: {await jobResp.Content.ReadAsStringAsync()}");
        var jobId = await jobResp.Content.ReadFromJsonAsync<Guid>();

        // 3. Assign vehicle
        var assignResp = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign", new { JobId = jobId });
        assignResp.IsSuccessStatusCode.Should().BeTrue($"Assign failed: {await assignResp.Content.ReadAsStringAsync()}");

        // 4. Commit plan
        var commitResp = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/commit", new { JobId = jobId });
        commitResp.IsSuccessStatusCode.Should().BeTrue($"Commit failed: {await commitResp.Content.ReadAsStringAsync()}");

        // 5. Dispatch trip
        var tripResp = await client.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = jobId,
            VehicleId = vehicleId,
            Legs = DtmsWebApplicationFactory.BuildSingleLeg(pickupId, dropId)
        });
        tripResp.IsSuccessStatusCode.Should().BeTrue($"Create trip failed: {await tripResp.Content.ReadAsStringAsync()}");
        var tripId = await tripResp.Content.ReadFromJsonAsync<Guid>();

        // 6. Get trip → collect all task IDs in sequence order
        var tripBody = await (await client.GetAsync($"/api/dispatch/trips/{tripId}")).Content.ReadAsStringAsync();
        var taskIds = ParseTaskIds(tripBody);
        taskIds.Should().NotBeEmpty("trip must have at least one task");

        // 7. Complete each task in order → trip auto-completes when last task done
        foreach (var taskId in taskIds)
        {
            var completeResp = await client.PostAsync(
                $"/api/dispatch/trips/{tripId}/tasks/{taskId}/complete", null);
            completeResp.IsSuccessStatusCode.Should().BeTrue(
                $"Complete task {taskId} failed: {await completeResp.Content.ReadAsStringAsync()}");
        }

        // 8. Verify trip status = Completed (enum value 3)
        var finalBody = await (await client.GetAsync($"/api/dispatch/trips/{tripId}")).Content.ReadAsStringAsync();
        finalBody.Should().Contain("\"status\":\"Completed\"", "trip must reach Completed status after all tasks done");
    }

    [Fact]
    public async Task Pipeline_TripCompleted_WritesToOutbox()
    {
        var client = await _factory.GetAuthenticatedClient();

        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var vehicleTypeId = await _factory.CreateVehicleTypeAsync();
        var vehicleId = await RegisterAndSetIdleAsync(client, vehicleTypeId);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, profileCode);

        var jobResp = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = orderId,
            PickupStationId = pickupId,
            DropStationId = dropId,
            Priority = "Normal"
        });
        var jobId = await jobResp.Content.ReadFromJsonAsync<Guid>();
        await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign", new { JobId = jobId });
        await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/commit", new { JobId = jobId });

        var tripResp = await client.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = jobId,
            VehicleId = vehicleId,
            Legs = DtmsWebApplicationFactory.BuildSingleLeg(pickupId, dropId)
        });
        var tripId = await tripResp.Content.ReadFromJsonAsync<Guid>();

        var tripBody = await (await client.GetAsync($"/api/dispatch/trips/{tripId}")).Content.ReadAsStringAsync();
        var taskIds = ParseTaskIds(tripBody);

        foreach (var taskId in taskIds)
            await client.PostAsync($"/api/dispatch/trips/{tripId}/tasks/{taskId}/complete", null);

        // Verify TripCompletedIntegrationEvent written to outbox
        using var scope = _factory.Services.CreateScope();
        var dispatchDb = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
        var messages = await dispatchDb.OutboxMessages
            .Where(m => m.Type.Contains("TripCompletedIntegrationEvent"))
            .ToListAsync();

        messages.Should().NotBeEmpty("completing all tasks must publish TripCompletedIntegrationEvent");
        messages.Should().Contain(m => m.Content.Contains(tripId.ToString()),
            "event content must reference the completed trip");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private Task<Guid> SubmitOrderAsync(HttpClient client, Guid pickupId, Guid dropId, string profileCode)
        => _factory.CreateAndSubmitOrderAsync(client, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddHours(4));

    private async Task<Guid> RegisterAndSetIdleAsync(HttpClient client, Guid vehicleTypeId)
    {
        var regResp = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = $"E2E-{Guid.NewGuid():N}"[..20],
            VehicleTypeId = vehicleTypeId,
            AdapterKey = "sim"  // Simulator — no real RIOT3 calls, no webhook race
        });
        regResp.IsSuccessStatusCode.Should().BeTrue();
        var vehicleId = await regResp.Content.ReadFromJsonAsync<Guid>();

        var stateResp = await client.PutAsJsonAsync($"/api/fleet/vehicles/{vehicleId}/state", new
        {
            VehicleId = vehicleId,
            NewState = 1,
            BatteryLevel = 90.0,
            CurrentNodeId = (Guid?)null
        });
        stateResp.IsSuccessStatusCode.Should().BeTrue();
        return vehicleId;
    }

    private static List<Guid> ParseTaskIds(string tripJson)
    {
        using var doc = JsonDocument.Parse(tripJson);
        return doc.RootElement.GetProperty("tasks")
            .EnumerateArray()
            .OrderBy(t => t.GetProperty("sequenceOrder").GetInt32())
            .Select(t => Guid.Parse(t.GetProperty("id").GetString()!))
            .ToList();
    }
}
