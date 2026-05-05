using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Integration tests for the OMS source adapter endpoint POST /api/oms/orders.
/// OMS endpoint uses API key auth — bypassed in Development via DisableApiKey=true.
/// TenantId is carried in the OMS request body (not JWT).
/// </summary>
public class OmsIntegrationTests : IClassFixture<DtmsWebApplicationFactory>
{
    private static readonly Guid OmsTenantId = new("cccccccc-0000-0000-0000-000000000003");

    private readonly DtmsWebApplicationFactory _factory;

    public OmsIntegrationTests(DtmsWebApplicationFactory factory) => _factory = factory;

    // ── Submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OmsOrder_SubmitValidOrder_Returns201WithOrderId()
    {
        var client = _factory.CreateClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(
            await _factory.GetAuthenticatedClient());

        var response = await client.PostAsJsonAsync("/api/oms/orders", BuildRequest(pickupId, dropId));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Valid OMS order must return 201: {await response.Content.ReadAsStringAsync()}");

        var orderId = await response.Content.ReadFromJsonAsync<Guid>();
        orderId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task OmsOrder_SubmitOrder_IsRetrievableViaDeliveryOrderEndpoint()
    {
        var adminClient = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(adminClient);

        var omsClient = _factory.CreateClient();
        var submitResp = await omsClient.PostAsJsonAsync("/api/oms/orders", BuildRequest(pickupId, dropId));
        submitResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = await submitResp.Content.ReadFromJsonAsync<Guid>();

        // Retrieve via delivery-orders endpoint using tenant-scoped JWT
        var tenantClient = await _factory.GetClientForTenantAsync(OmsTenantId);
        var getResp = await tenantClient.GetAsync($"/api/delivery-orders/{orderId}");

        getResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Order submitted via OMS must be retrievable: {await getResp.Content.ReadAsStringAsync()}");

        var body = await getResp.Content.ReadAsStringAsync();
        body.Should().Contain("OMS-2026-TEST", "OrderNo must match OmsOrderReference");
        body.Should().Contain("ReadyToPlan", "Order must progress through validation");
    }

    [Fact]
    public async Task OmsOrder_MultipleItemsSamePickupDrop_GroupedIntoOneLeg()
    {
        var adminClient = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(adminClient);

        var omsClient = _factory.CreateClient();
        var request = BuildRequest(pickupId, dropId, omsOrderId: 30001, extraItems: true);
        var submitResp = await omsClient.PostAsJsonAsync("/api/oms/orders", request);
        submitResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = await submitResp.Content.ReadFromJsonAsync<Guid>();

        var tenantClient = await _factory.GetClientForTenantAsync(OmsTenantId);
        var getResp = await tenantClient.GetAsync($"/api/delivery-orders/{orderId}");
        var body = await getResp.Content.ReadAsStringAsync();

        // 3 items same pickup/drop → 1 leg
        body.Should().Contain("\"legs\"");
        body.Should().Contain("ENG-001");
        body.Should().Contain("GSK-101");
        body.Should().Contain("BLT-201");
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task OmsOrder_SameOmsOrderId_ReturnsSameOrderId()
    {
        var adminClient = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(adminClient);

        var omsClient = _factory.CreateClient();
        var request = BuildRequest(pickupId, dropId, omsOrderId: 40001);

        var first = await omsClient.PostAsJsonAsync("/api/oms/orders", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = await first.Content.ReadFromJsonAsync<Guid>();

        var second = await omsClient.PostAsJsonAsync("/api/oms/orders", request);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "Duplicate OmsOrderId must return cached result");
        var secondId = await second.Content.ReadFromJsonAsync<Guid>();

        secondId.Should().Be(firstId, "Same OmsOrderId + TenantId must return same OrderId");
    }

    [Fact]
    public async Task OmsOrder_DifferentOmsOrderId_ReturnsDifferentOrderId()
    {
        var adminClient = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(adminClient);

        var omsClient = _factory.CreateClient();

        var first = await omsClient.PostAsJsonAsync("/api/oms/orders",
            BuildRequest(pickupId, dropId, omsOrderId: 50001));
        var second = await omsClient.PostAsJsonAsync("/api/oms/orders",
            BuildRequest(pickupId, dropId, omsOrderId: 50002));

        first.IsSuccessStatusCode.Should().BeTrue();
        second.IsSuccessStatusCode.Should().BeTrue();

        var firstId = await first.Content.ReadFromJsonAsync<Guid>();
        var secondId = await second.Content.ReadFromJsonAsync<Guid>();

        secondId.Should().NotBe(firstId, "Different OmsOrderId must produce distinct orders");
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OmsOrder_EmptyLines_Returns400()
    {
        var omsClient = _factory.CreateClient();

        var response = await omsClient.PostAsJsonAsync("/api/oms/orders", new
        {
            tenantId = OmsTenantId,
            omsOrderId = 60001,
            omsOrderReference = "OMS-INVALID-001",
            requestedBy = "test-user",
            urgencyCode = "NORMAL",
            requiredByDate = DateTime.UtcNow.AddHours(4),
            lines = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "OMS order with no lines must be rejected");
    }

    [Fact]
    public async Task OmsOrder_SlaTooSoon_Returns400()
    {
        var adminClient = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(adminClient);

        var omsClient = _factory.CreateClient();
        var request = BuildRequest(pickupId, dropId, omsOrderId: 70001,
            requiredByDate: DateTime.UtcNow.AddMinutes(5));

        var response = await omsClient.PostAsJsonAsync("/api/oms/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "OMS order with SLA less than minimum lead time must be rejected");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static object BuildRequest(
        Guid pickupId,
        Guid dropId,
        int omsOrderId = 20001,
        bool extraItems = false,
        DateTime? requiredByDate = null) => new
    {
        tenantId = OmsTenantId,
        omsOrderId,
        omsOrderReference = "OMS-2026-TEST",
        requestedBy = "oms-system",
        urgencyCode = "NORMAL",
        requiredByDate = requiredByDate ?? DateTime.UtcNow.AddHours(4),
        lines = extraItems
            ? new[]
            {
                BuildLine(pickupId, dropId, 9001, "ENG-001", "Engine Block"),
                BuildLine(pickupId, dropId, 9002, "GSK-101", "Gasket Set"),
                BuildLine(pickupId, dropId, 9003, "BLT-201", "Bolt Set")
            }
            : new[]
            {
                BuildLine(pickupId, dropId, 9001, "ENG-001", "Engine Block")
            }
    };

    private static object BuildLine(
        Guid pickupId, Guid dropId,
        int materialId, string materialCode, string materialDescription) => new
    {
        sourceLocation = pickupId.ToString(),
        destinationLocation = dropId.ToString(),
        workOrderId = 5001,
        workOrderRef = "WO-2026-0501",
        materialId,
        materialCode,
        materialDescription,
        qty = 1.0,
        weightKg = 10.0,
        productLine = "Line-1",
        productModel = "Fortuner",
        note = (string?)null
    };
}
