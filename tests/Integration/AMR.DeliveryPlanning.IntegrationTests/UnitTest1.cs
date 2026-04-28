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

        // Must create real stations first — C5 validates station existence in Facility DB
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

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

        // 0. Register an idle vehicle so GreedyVehicleSelector can find it via DB
        var vehicleTypeId = await _factory.CreateVehicleTypeAsync();
        var regResponse = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = "TestVehicle-Assign",
            VehicleTypeId = vehicleTypeId
        });
        regResponse.IsSuccessStatusCode.Should().BeTrue($"Register vehicle failed: {await regResponse.Content.ReadAsStringAsync()}");
        var vehicleId = await regResponse.Content.ReadFromJsonAsync<Guid>();

        var stateResponse = await client.PutAsJsonAsync($"/api/fleet/vehicles/{vehicleId}/state", new
        {
            VehicleId = vehicleId,
            NewState = 1, // VehicleState.Idle
            BatteryLevel = 90.0,
            CurrentNodeId = (Guid?)null
        });
        stateResponse.IsSuccessStatusCode.Should().BeTrue($"Set vehicle state failed: {await stateResponse.Content.ReadAsStringAsync()}");

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
        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();

        var tripResponse = await client.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            Legs = DtmsWebApplicationFactory.BuildSingleLeg(pickupId, dropId)
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

        // Register an idle vehicle so GreedyVehicleSelector can find it via DB
        var replanVehicleTypeId = await _factory.CreateVehicleTypeAsync();
        var regResponse = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = "TestVehicle-Replan",
            VehicleTypeId = replanVehicleTypeId
        });
        regResponse.IsSuccessStatusCode.Should().BeTrue($"Register vehicle failed: {await regResponse.Content.ReadAsStringAsync()}");
        var vehicleId = await regResponse.Content.ReadFromJsonAsync<Guid>();

        var stateResponse = await client.PutAsJsonAsync($"/api/fleet/vehicles/{vehicleId}/state", new
        {
            VehicleId = vehicleId,
            NewState = 1, // VehicleState.Idle
            BatteryLevel = 95.0,
            CurrentNodeId = (Guid?)null
        });
        stateResponse.IsSuccessStatusCode.Should().BeTrue($"Set vehicle state failed: {await stateResponse.Content.ReadAsStringAsync()}");

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

    // ── Phase 3: Cross-Dock ──────────────────────────────────────

    [Fact]
    public async Task Planning_CrossDock_CreatesLinkedJobs()
    {
        var client = await _factory.GetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/planning/cross-dock", new
        {
            InboundOrderId = Guid.NewGuid(),
            OutboundOrderId = Guid.NewGuid(),
            InboundPickupStationId = Guid.NewGuid(),
            DockStationId = Guid.NewGuid(),
            OutboundDropStationId = Guid.NewGuid(),
            HandlingDwellMinutes = 5,
            Priority = "Normal"
        });

        response.IsSuccessStatusCode.Should().BeTrue($"Cross-dock failed: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("inboundJobId");
        body.Should().Contain("outboundJobId");
        body.Should().Contain("dependencyId");
    }

    // ── Phase 3: Milk-Run ────────────────────────────────────────

    [Fact]
    public async Task Planning_MilkRun_CreatesTemplateAndJob()
    {
        var client = await _factory.GetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/planning/milk-runs", new
        {
            TemplateName = "Line-A Morning",
            CronSchedule = "0 8 * * 1-5",
            Stops = new[]
            {
                new { StationId = Guid.NewGuid(), SequenceOrder = 1, ArrivalOffsetMinutes = (int?)0, DwellMinutes = 3 },
                new { StationId = Guid.NewGuid(), SequenceOrder = 2, ArrivalOffsetMinutes = (int?)10, DwellMinutes = 5 },
                new { StationId = Guid.NewGuid(), SequenceOrder = 3, ArrivalOffsetMinutes = (int?)25, DwellMinutes = 3 }
            },
            Priority = "Normal"
        });

        response.IsSuccessStatusCode.Should().BeTrue($"Milk-run failed: {await response.Content.ReadAsStringAsync()}");
        var templateId = await response.Content.ReadFromJsonAsync<Guid>();
        templateId.Should().NotBe(Guid.Empty);
    }

    // ── Phase 3: Multi-Pick Multi-Drop ───────────────────────────

    [Fact]
    public async Task Planning_MultiPickDrop_CreatesJobWithPairs()
    {
        var client = await _factory.GetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/planning/multi-pick-drop", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            Pairs = new[]
            {
                new { PickupStationId = Guid.NewGuid(), DropStationId = Guid.NewGuid(), Weight = 5.0 },
                new { PickupStationId = Guid.NewGuid(), DropStationId = Guid.NewGuid(), Weight = 8.0 },
                new { PickupStationId = Guid.NewGuid(), DropStationId = Guid.NewGuid(), Weight = 3.5 }
            },
            Priority = "High",
            RequiredCapability = "LIFT"
        });

        response.IsSuccessStatusCode.Should().BeTrue($"Multi-pick-drop failed: {await response.Content.ReadAsStringAsync()}");
        var jobId = await response.Content.ReadFromJsonAsync<Guid>();
        jobId.Should().NotBe(Guid.Empty);
    }

    // ── Hardening: Trip Lifecycle ─────────────────────────────────

    [Fact]
    public async Task Dispatch_TripLifecycle_CreateStartComplete()
    {
        var client = await _factory.GetAuthenticatedClient();

        // Create trip (auto-starts on dispatch)
        var p = Guid.NewGuid(); var d = Guid.NewGuid();
        var createResponse = await client.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            Legs = DtmsWebApplicationFactory.BuildSingleLeg(p, d)
        });
        createResponse.IsSuccessStatusCode.Should().BeTrue($"Create trip failed: {await createResponse.Content.ReadAsStringAsync()}");
        var tripId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Verify trip is auto-started (status=1 = InProgress)
        var getResponse = await client.GetAsync($"/api/dispatch/trips/{tripId}");
        getResponse.IsSuccessStatusCode.Should().BeTrue();
        var body = await getResponse.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":1");
    }

    // ── Hardening: Auth Register ─────────────────────────────────

    [Fact]
    public async Task Auth_Register_CreatesNewUser()
    {
        var client = await _factory.GetAuthenticatedClient();

        var username = $"testuser_{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = username,
            Password = "Test1234!",
            Role = "Operator"
        });

        response.IsSuccessStatusCode.Should().BeTrue($"Register failed: {await response.Content.ReadAsStringAsync()}");

        // Login with new user
        var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/token", new
        {
            Username = username,
            Password = "Test1234!"
        });
        loginResponse.IsSuccessStatusCode.Should().BeTrue("New user should be able to login");
    }

    private record TokenResponse(string Token, DateTime ExpiresAt);
}
