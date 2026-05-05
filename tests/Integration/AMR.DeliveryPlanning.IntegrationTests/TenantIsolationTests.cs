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

        // Tenant B creates an order — stations are shared (facility is not tenant-scoped)
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(clientA);

        var orderResp = await clientB.PostAsJsonAsync("/api/delivery-orders", new
        {
            OrderId = 3001,
            OrderNo = $"ISO-B-{Guid.NewGuid():N}",
            CreateBy = "test-user",
            PickupLocationCode = pickupId.ToString(),
            DropLocationCode = dropId.ToString(),
            Priority = 0,
            SLA = DateTime.UtcNow.AddHours(4),
            OrderItems = new[] { new { ItemCode = "X", Quantity = 1, Weight = 1.0, Remarks = (string?)null } }
        });
        orderResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant B should be able to submit an order: {await orderResp.Content.ReadAsStringAsync()}");
        var orderId = await orderResp.Content.ReadFromJsonAsync<Guid>();

        // Tenant A tries to access the order created by Tenant B — query filter returns 404
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

        // Each tenant creates one order
        var keyA = $"ISO-LIST-A-{Guid.NewGuid():N}";
        var keyB = $"ISO-LIST-B-{Guid.NewGuid():N}";

        var orderAResp = await clientA.PostAsJsonAsync("/api/delivery-orders", new
        {
            OrderId = 3002,
            OrderNo = keyA,
            CreateBy = "test-user",
            PickupLocationCode = pickupId.ToString(),
            DropLocationCode = dropId.ToString(),
            Priority = 0,
            SLA = DateTime.UtcNow.AddHours(4),
            OrderItems = new[] { new { ItemCode = "A", Quantity = 1, Weight = 1.0, Remarks = (string?)null } }
        });
        orderAResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant A order creation failed: {await orderAResp.Content.ReadAsStringAsync()}");

        var orderBResp = await clientB.PostAsJsonAsync("/api/delivery-orders", new
        {
            OrderId = 3003,
            OrderNo = keyB,
            CreateBy = "test-user",
            PickupLocationCode = pickupId.ToString(),
            DropLocationCode = dropId.ToString(),
            Priority = 0,
            SLA = DateTime.UtcNow.AddHours(4),
            OrderItems = new[] { new { ItemCode = "B", Quantity = 1, Weight = 1.0, Remarks = (string?)null } }
        });
        orderBResp.IsSuccessStatusCode.Should().BeTrue(
            $"Tenant B order creation failed: {await orderBResp.Content.ReadAsStringAsync()}");

        // Tenant B lists orders (SubmitDeliveryOrder advances to ReadyToPlan, so query that status)
        var listResp = await clientB.GetAsync("/api/delivery-orders?page=1&pageSize=50&status=ReadyToPlan");
        listResp.IsSuccessStatusCode.Should().BeTrue($"List orders failed: {await listResp.Content.ReadAsStringAsync()}");
        var body = await listResp.Content.ReadAsStringAsync();
        body.Should().Contain(keyB, "Tenant B should see its own order");
        body.Should().NotContain(keyA, "Tenant B must not see Tenant A's order");
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
