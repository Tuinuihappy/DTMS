using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Verifies that the EF global query filters prevent cross-tenant data access.
/// Each test creates two isolated tenants and confirms Tenant A cannot read or
/// mutate Tenant B's resources.
/// </summary>
public class TenantIsolationTests : IClassFixture<DtmsWebApplicationFactory>
{
    private static readonly Guid TenantA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = new("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly DtmsWebApplicationFactory _factory;

    public TenantIsolationTests(DtmsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Delivery Order isolation ──────────────────────────────────────────────

    [Fact]
    public async Task DeliveryOrder_TenantA_CannotSee_TenantB_Order()
    {
        var clientA = await _factory.GetClientForTenantAsync(TenantA);
        var clientB = await _factory.GetClientForTenantAsync(TenantB);

        var (pickupId, dropId) = await _factory.CreateStationPairAsync(clientA);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(clientA);

        var orderResp = await clientB.PostAsJsonAsync("/api/delivery-orders",
            DtmsWebApplicationFactory.BuildOrderRequest(pickupId, dropId, profileCode,
                sla: DateTime.UtcNow.AddHours(4)));
        orderResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant B should be able to submit an order: {await orderResp.Content.ReadAsStringAsync()}");
        var orderId = await orderResp.Content.ReadFromJsonAsync<Guid>();

        var getResp = await clientA.GetAsync($"/api/delivery-orders/{orderId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "EF global query filter must prevent Tenant A from reading Tenant B's order");
    }

    [Fact]
    public async Task DeliveryOrder_TenantB_CanSee_OwnOrders_ButNotTenantA()
    {
        var clientA = await _factory.GetClientForTenantAsync(TenantA);
        var clientB = await _factory.GetClientForTenantAsync(TenantB);

        var (pickupId, dropId) = await _factory.CreateStationPairAsync(clientA);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(clientA);

        var nameA = $"ISO-A-{Guid.NewGuid():N}"[..20];
        var nameB = $"ISO-B-{Guid.NewGuid():N}"[..20];

        await _factory.CreateAndSubmitOrderAsync(clientA, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddHours(4), orderName: nameA);

        await _factory.CreateAndSubmitOrderAsync(clientB, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddHours(4), orderName: nameB);

        var listResp = await clientB.GetAsync("/api/delivery-orders?page=1&pageSize=50&status=ReadyToPlan");
        listResp.IsSuccessStatusCode.Should().BeTrue($"List orders failed: {await listResp.Content.ReadAsStringAsync()}");
        var body = await listResp.Content.ReadAsStringAsync();
        body.Should().Contain(nameB, "Tenant B should see its own order");
        body.Should().NotContain(nameA, "Tenant B must not see Tenant A's order");
    }

    // ── Trip isolation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Trip_TenantA_CannotSee_TenantB_Trip()
    {
        var clientA = await _factory.GetClientForTenantAsync(TenantA);
        var clientB = await _factory.GetClientForTenantAsync(TenantB);

        // Tenant B creates a trip
        var tripResp = await clientB.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            Legs = DtmsWebApplicationFactory.BuildSingleLeg(Guid.NewGuid(), Guid.NewGuid())
        });
        tripResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant B should be able to create a trip: {await tripResp.Content.ReadAsStringAsync()}");
        var tripId = await tripResp.Content.ReadFromJsonAsync<Guid>();

        // Tenant A tries to read Tenant B's trip
        var getResp = await clientA.GetAsync($"/api/dispatch/trips/{tripId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "EF global query filter must prevent Tenant A from reading Tenant B's trip");
    }

    // ── Vehicle isolation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Vehicle_TenantA_CannotSee_TenantB_Vehicle()
    {
        var clientA = await _factory.GetClientForTenantAsync(TenantA);
        var clientB = await _factory.GetClientForTenantAsync(TenantB);

        // Tenant B registers a vehicle (VehicleType must exist — created via direct DB insert)
        var vtId = await _factory.CreateVehicleTypeAsync();
        var vehicleResp = await clientB.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = $"VehicleB-{Guid.NewGuid():N}"[..30],
            VehicleTypeId = vtId
        });
        vehicleResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant B should be able to register a vehicle: {await vehicleResp.Content.ReadAsStringAsync()}");
        var vehicleId = await vehicleResp.Content.ReadFromJsonAsync<Guid>();

        // Tenant A tries to read Tenant B's vehicle
        var getResp = await clientA.GetAsync($"/api/fleet/vehicles/{vehicleId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "EF global query filter must prevent Tenant A from reading Tenant B's vehicle");
    }

    // ── Planning isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Job_TenantA_CannotSee_TenantB_Job()
    {
        var clientA = await _factory.GetClientForTenantAsync(TenantA);
        var clientB = await _factory.GetClientForTenantAsync(TenantB);

        // Tenant B creates a planning job
        var jobResp = await clientB.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = Guid.NewGuid(),
            DropStationId = Guid.NewGuid(),
            Priority = "Normal"
        });
        jobResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant B should be able to create a job: {await jobResp.Content.ReadAsStringAsync()}");
        var jobId = await jobResp.Content.ReadFromJsonAsync<Guid>();

        // Tenant A tries to read Tenant B's job
        var getResp = await clientA.GetAsync($"/api/planning/jobs/{jobId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "EF global query filter must prevent Tenant A from reading Tenant B's job");
    }
}
