using System.Net.Http.Json;
using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #7: Capability-based vehicle assignment.
/// LIFT job → only LIFT-capable vehicle selected; non-LIFT vehicle is rejected.
/// Uses separate tenants to guarantee vehicle isolation between tests.
/// </summary>
public class CapabilityAssignmentTests : IClassFixture<DtmsWebApplicationFactory>
{
    // Dedicated tenants prevent vehicles created in one test from appearing in another
    private static readonly Guid LiftTenant = new("cccccccc-0000-0000-0000-000000000010");
    private static readonly Guid NoLiftTenant = new("dddddddd-0000-0000-0000-000000000020");

    private readonly DtmsWebApplicationFactory _factory;

    public CapabilityAssignmentTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Assign_LiftJob_WithLiftVehicleAvailable_Succeeds()
    {
        var client = await _factory.GetClientForTenantAsync(LiftTenant);

        // Register a vehicle with LIFT + MOVE capabilities
        var liftTypeId = await CreateVehicleTypeAsync(["LIFT", "MOVE"]);
        var vehicleId = await RegisterAndSetIdleAsync(client, liftTypeId);

        // Create job requiring LIFT capability
        var jobId = await CreateJobWithCapabilityAsync(client, "LIFT");

        // Assign — GreedyVehicleSelector must find the LIFT vehicle
        var assignResp = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign",
            new { JobId = jobId });

        assignResp.IsSuccessStatusCode.Should().BeTrue(
            $"Assign must succeed when a LIFT vehicle is available: {await assignResp.Content.ReadAsStringAsync()}");

        // Verify job is now in Assigned status by reading it
        var getResp = await client.GetAsync($"/api/planning/jobs/{jobId}");
        getResp.IsSuccessStatusCode.Should().BeTrue();
        var body = await getResp.Content.ReadAsStringAsync();
        body.Should().Contain(vehicleId.ToString(), "assigned vehicle must be the LIFT-capable one");
    }

    [Fact]
    public async Task Assign_LiftJob_WithOnlyMoveVehicle_Fails()
    {
        var client = await _factory.GetClientForTenantAsync(NoLiftTenant);

        // Register a vehicle with MOVE capability only — does NOT satisfy LIFT requirement
        var moveTypeId = await CreateVehicleTypeAsync(["MOVE"]);
        await RegisterAndSetIdleAsync(client, moveTypeId);

        // Create job requiring LIFT capability
        var jobId = await CreateJobWithCapabilityAsync(client, "LIFT");

        // Assign — no LIFT vehicle available for this tenant → must fail
        var assignResp = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign",
            new { JobId = jobId });

        assignResp.IsSuccessStatusCode.Should().BeFalse(
            "no LIFT vehicle is registered for this tenant — assign must fail");
    }

    [Fact]
    public async Task Assign_JobWithNoCapabilityRequirement_AnyVehicleMatches()
    {
        var client = await _factory.GetAuthenticatedClient();
        var moveTypeId = await CreateVehicleTypeAsync(["MOVE"]);
        await RegisterAndSetIdleAsync(client, moveTypeId);

        // Job with no RequiredCapability — any idle vehicle is eligible
        var jobResp = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal"
            // RequiredCapability omitted
        });
        jobResp.IsSuccessStatusCode.Should().BeTrue();
        var jobId = await jobResp.Content.ReadFromJsonAsync<Guid>();

        var assignResp = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign",
            new { JobId = jobId });

        assignResp.IsSuccessStatusCode.Should().BeTrue(
            $"Job without capability constraint must be assignable to any idle vehicle: {await assignResp.Content.ReadAsStringAsync()}");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> CreateVehicleTypeAsync(string[] capabilities)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var vehicleType = new VehicleType(
            Guid.NewGuid(),
            $"Type-{string.Join("-", capabilities)}-{Guid.NewGuid():N}"[..40],
            100.0,
            capabilities);
        db.VehicleTypes.Add(vehicleType);
        await db.SaveChangesAsync();
        return vehicleType.Id;
    }

    private static async Task<Guid> RegisterAndSetIdleAsync(HttpClient client, Guid vehicleTypeId)
    {
        var regResp = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = $"CAP-{Guid.NewGuid():N}"[..20],
            VehicleTypeId = vehicleTypeId
        });
        regResp.IsSuccessStatusCode.Should().BeTrue(
            $"Vehicle registration failed: {await regResp.Content.ReadAsStringAsync()}");
        var vehicleId = await regResp.Content.ReadFromJsonAsync<Guid>();

        var stateResp = await client.PutAsJsonAsync($"/api/fleet/vehicles/{vehicleId}/state", new
        {
            VehicleId = vehicleId,
            NewState = 1,   // Idle
            BatteryLevel = 90.0,
            CurrentNodeId = (Guid?)null
        });
        stateResp.IsSuccessStatusCode.Should().BeTrue(
            $"Set vehicle state failed: {await stateResp.Content.ReadAsStringAsync()}");

        return vehicleId;
    }

    private static async Task<Guid> CreateJobWithCapabilityAsync(HttpClient client, string capability)
    {
        var resp = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal",
            RequiredCapability = capability
        });
        resp.IsSuccessStatusCode.Should().BeTrue(
            $"Create job with capability={capability} failed: {await resp.Content.ReadAsStringAsync()}");
        return await resp.Content.ReadFromJsonAsync<Guid>();
    }
}
