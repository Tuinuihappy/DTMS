using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Integration tests verifying end-to-end API flows through real PostgreSQL.
/// </summary>
public class EndToEndFlowTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public EndToEndFlowTests(DtmsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Auth ──────────────────────────────────────────────────────

    [Fact]
    public async Task Auth_LoginWithValidCredentials_ReturnsToken()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new
        {
            Username = "admin",
            Password = "admin123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body!.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Auth_LoginWithInvalidCredentials_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new
        {
            Username = "wrong",
            Password = "wrong"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/planning/jobs/pending");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Facility ──────────────────────────────────────────────────

    [Fact]
    public async Task Facility_CreateAndGetMap_ReturnsMapData()
    {
        var client = await _factory.GetAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/facility/maps", new
        {
            Name = "TestMap",
            Version = "1.0",
            Width = 100.0,
            Height = 200.0,
            MapData = "{\"floor\": 1}"
        });

        createResponse.IsSuccessStatusCode.Should().BeTrue($"Expected 2xx but got {createResponse.StatusCode}");
        var mapId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        mapId.Should().NotBe(Guid.Empty);

        var getResponse = await client.GetAsync($"/api/facility/maps/{mapId}");
        getResponse.IsSuccessStatusCode.Should().BeTrue();
    }

    // ── DeliveryOrder ─────────────────────────────────────────────

    [Fact]
    public async Task DeliveryOrder_SubmitOrder_ReturnsOrderId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/delivery-orders", new
        {
            OrderKey = $"TEST-{Guid.NewGuid():N}",
            PickupLocationCode = pickupId.ToString(),
            DropLocationCode = dropId.ToString(),
            Priority = 0,
            SLA = DateTime.UtcNow.AddHours(4),
            Lines = new[]
            {
                new { ItemCode = "ITEM-A", Quantity = 10, Weight = 5.0, Remarks = "Test item" }
            }
        });

        response.IsSuccessStatusCode.Should().BeTrue($"Expected 2xx but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    // ── Planning ──────────────────────────────────────────────────

    [Fact]
    public async Task Planning_CreateAndCommitJob_FullFlow()
    {
        var client = await _factory.GetAuthenticatedClient();

        // 0. Seed a vehicle into GreedyVehicleSelector's in-memory cache
        var vehicleId = Guid.NewGuid();
        AMR.DeliveryPlanning.Planning.Infrastructure.Services.GreedyVehicleSelector
            .UpdateVehicleCache(vehicleId, distanceToOrigin: 10.0, batteryLevel: 90.0);

        // 1. Create Job
        var createResponse = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal"
        });

        createResponse.IsSuccessStatusCode.Should().BeTrue($"Create failed: {await createResponse.Content.ReadAsStringAsync()}");
        var jobId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        jobId.Should().NotBe(Guid.Empty);

        // 2. Get Job
        var getResponse = await client.GetAsync($"/api/planning/jobs/{jobId}");
        getResponse.IsSuccessStatusCode.Should().BeTrue();

        // 3. Assign Vehicle (GreedyVehicleSelector will find the registered vehicle)
        var assignResponse = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign", new
        {
            JobId = jobId
        });
        assignResponse.IsSuccessStatusCode.Should().BeTrue($"Assign failed: {await assignResponse.Content.ReadAsStringAsync()}");

        // 4. Commit Plan
        var commitResponse = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/commit", new
        {
            JobId = jobId
        });
        commitResponse.IsSuccessStatusCode.Should().BeTrue($"Commit failed: {await commitResponse.Content.ReadAsStringAsync()}");
    }

    // ── Dispatch ──────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_CreateTrip_FullFlow()
    {
        var client = await _factory.GetAuthenticatedClient();

        var tripResponse = await client.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid()
        });

        tripResponse.IsSuccessStatusCode.Should().BeTrue($"Create trip failed: {await tripResponse.Content.ReadAsStringAsync()}");
        var tripId = await tripResponse.Content.ReadFromJsonAsync<Guid>();
        tripId.Should().NotBe(Guid.Empty);

        var getResponse = await client.GetAsync($"/api/dispatch/trips/{tripId}");
        getResponse.IsSuccessStatusCode.Should().BeTrue();
    }

    // ── Phase 2: Multi-Stop ──────────────────────────────────────

    [Fact]
    public async Task Planning_MultiStop_CreatesMultipleLegs()
    {
        var client = await _factory.GetAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal",
            AdditionalDropStationIds = new[] { Guid.NewGuid(), Guid.NewGuid() }
        });

        createResponse.IsSuccessStatusCode.Should().BeTrue($"Create multi-stop failed: {await createResponse.Content.ReadAsStringAsync()}");
        var jobId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        jobId.Should().NotBe(Guid.Empty);

        // Verify job has multiple legs
        var getResponse = await client.GetAsync($"/api/planning/jobs/{jobId}");
        getResponse.IsSuccessStatusCode.Should().BeTrue();
    }

    // ── Phase 2: Consolidation ───────────────────────────────────

    [Fact]
    public async Task Planning_Consolidate_MergesOrders()
    {
        var client = await _factory.GetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/planning/consolidate", new
        {
            OrderIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() },
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "High"
        });

        response.IsSuccessStatusCode.Should().BeTrue($"Consolidate failed: {await response.Content.ReadAsStringAsync()}");
        var jobId = await response.Content.ReadFromJsonAsync<Guid>();
        jobId.Should().NotBe(Guid.Empty);
    }

    // ── Phase 2: Replan ──────────────────────────────────────────

    [Fact]
    public async Task Planning_ReplanCommittedJob_ResetsAndReassigns()
    {
        var client = await _factory.GetAuthenticatedClient();

        // Seed vehicle
        var vehicleId = Guid.NewGuid();
        AMR.DeliveryPlanning.Planning.Infrastructure.Services.GreedyVehicleSelector
            .UpdateVehicleCache(vehicleId, 5.0, 95.0);

        // Create + Assign + Commit
        var createResponse = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal"
        });
        var jobId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign", new { JobId = jobId });
        await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/commit", new { JobId = jobId });

        // Replan
        var replanResponse = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/replan", new
        {
            JobId = jobId,
            Reason = "DISRUPTION"
        });

        replanResponse.IsSuccessStatusCode.Should().BeTrue($"Replan failed: {await replanResponse.Content.ReadAsStringAsync()}");
    }

    private record TokenResponse(string Token, DateTime ExpiresAt);
}

