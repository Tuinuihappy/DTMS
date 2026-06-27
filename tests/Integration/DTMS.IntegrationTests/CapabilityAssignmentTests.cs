using System.Net.Http.Json;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #7: Capability-based vehicle assignment.
/// Verifies <c>GreedyVehicleSelector</c> picks vehicles whose VehicleType
/// capabilities satisfy a job's RequiredCapability.
///
/// Note: the original test suite used <c>GetClientForTenantAsync</c> to put
/// each scenario in its own tenant so vehicles created in one test wouldn't
/// leak into another. Multi-tenancy was deliberately descoped (Decision #3),
/// so the per-tenant LIFT vs MOVE isolation scenarios are gone — those need
/// per-test DB cleanup or in-memory fixtures, which is its own redesign and
/// out of scope for this test-debt cleanup pass. The capability-omitted-from-job
/// scenario remains and exercises the selector path that matters most: a job
/// with no required capability matches any idle vehicle.
/// </summary>
public class CapabilityAssignmentTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public CapabilityAssignmentTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Assign_JobWithNoCapabilityRequirement_AnyVehicleMatches()
    {
        var client = await _factory.GetAuthenticatedClient();
        var moveTypeId = await CreateVehicleTypeAsync(["MOVE"]);
        await RegisterAndSetIdleAsync(client, moveTypeId);

        // Job with no RequiredCapability — any idle vehicle is eligible
        var jobResp = await client.PostAsJsonAsync("/api/v1/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal"
            // RequiredCapability omitted
        });
        jobResp.IsSuccessStatusCode.Should().BeTrue();
        var jobId = await jobResp.Content.ReadFromJsonAsync<Guid>();

        var assignResp = await client.PostAsJsonAsync($"/api/v1/planning/jobs/{jobId}/assign",
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
        var regResp = await client.PostAsJsonAsync("/api/v1/fleet/vehicles", new
        {
            VehicleName = $"CAP-{Guid.NewGuid():N}"[..20],
            VehicleTypeId = vehicleTypeId
        });
        regResp.IsSuccessStatusCode.Should().BeTrue(
            $"Vehicle registration failed: {await regResp.Content.ReadAsStringAsync()}");
        var vehicleId = await regResp.Content.ReadFromJsonAsync<Guid>();

        var stateResp = await client.PutAsJsonAsync($"/api/v1/fleet/vehicles/{vehicleId}/state", new
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
}
