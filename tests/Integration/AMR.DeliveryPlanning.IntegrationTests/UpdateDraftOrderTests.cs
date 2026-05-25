using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

public class UpdateDraftOrderTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public UpdateDraftOrderTests(DtmsWebApplicationFactory factory) => _factory = factory;

    // ── helpers ─────────────────────────────────────────────────────────────

    private static object CreateOrderBody(string orderRef = "ORD-UPDATE-TEST") => new
    {
        orderRef,
        priority = "Normal",
        cargoType = "FinishedGood",
        requestedDeliveryDate = (DateTime?)null,
        items = new[]
        {
            new
            {
                sku = "SKU-ORIG-001",
                pickupLocationCode = "WH-01",
                dropLocationCode = "LINE-01",
                dimensions = (object?)null,
                weightKg = 5.0,
                quantity = new { value = 10, uom = "PCS" },
                cargoSpecific = (object?)null
            }
        }
    };

    private static object UpdateOrderBody(string orderRef = "ORD-UPDATED") => new
    {
        orderRef,
        priority = "High",
        cargoType = "RawMaterial",
        requestedDeliveryDate = (DateTime?)null,
        items = new[]
        {
            new
            {
                sku = "SKU-NEW-001",
                pickupLocationCode = "WH-02",
                dropLocationCode = "LINE-05",
                dimensions = (object?)new { lengthMm = 300.0, widthMm = 200.0, heightMm = 100.0 },
                weightKg = 8.0,
                quantity = new { value = 3, uom = "BOX" },
                cargoSpecific = (object?)null
            },
            new
            {
                sku = "SKU-NEW-002",
                pickupLocationCode = "WH-02",
                dropLocationCode = "LINE-06",
                dimensions = (object?)null,
                weightKg = 2.0,
                quantity = new { value = 50, uom = "PCS" },
                cargoSpecific = (object?)null
            }
        }
    };

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDraft_ReplacesItemsAndFields()
    {
        var client = await _factory.GetAuthenticatedClient();

        var createResp = await client.PostAsJsonAsync("/api/v1/delivery-orders", CreateOrderBody());
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<OrderDetailResponse>();
        created.Should().NotBeNull();
        created!.Items.Should().HaveCount(1);

        var updateResp = await client.PutAsJsonAsync($"/api/v1/delivery-orders/{created.Id}", UpdateOrderBody());
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK, await updateResp.Content.ReadAsStringAsync());

        var updated = await updateResp.Content.ReadFromJsonAsync<OrderDetailResponse>();
        updated.Should().NotBeNull();
        updated!.Id.Should().Be(created.Id);
        updated.OrderRef.Should().Be("ORD-UPDATED");
        updated.Priority.Should().Be("High");
        updated.CargoType.Should().Be("RawMaterial");
        updated.Items.Should().HaveCount(2);
        updated.Items.Select(i => i.Sku).Should().BeEquivalentTo(["SKU-NEW-001", "SKU-NEW-002"]);
    }

    [Fact]
    public async Task UpdateDraft_AllowsReusingSku_AfterReplace()
    {
        var client = await _factory.GetAuthenticatedClient();

        var createResp = await client.PostAsJsonAsync("/api/v1/delivery-orders", CreateOrderBody());
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<OrderDetailResponse>();

        var reuseBody = new
        {
            orderRef = "ORD-REUSE",
            priority = "Normal",
            cargoType = "FinishedGood",
            requestedDeliveryDate = (DateTime?)null,
            items = new[]
            {
                new
                {
                    sku = "SKU-ORIG-001",  // same SKU as original — must be allowed
                    pickupLocationCode = "WH-03",
                    dropLocationCode = "LINE-07",
                    dimensions = (object?)null,
                    weightKg = 1.0,
                    quantity = new { value = 1, uom = "PCS" },
                    cargoSpecific = (object?)null
                }
            }
        };

        var updateResp = await client.PutAsJsonAsync($"/api/v1/delivery-orders/{created!.Id}", reuseBody);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK, await updateResp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateDraft_WhenOrderNotFound_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();

        var updateResp = await client.PutAsJsonAsync($"/api/v1/delivery-orders/{Guid.NewGuid()}", UpdateOrderBody());

        updateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateDraft_WithEmptyItems_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();

        var createResp = await client.PostAsJsonAsync("/api/v1/delivery-orders", CreateOrderBody());
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<OrderDetailResponse>();

        var invalidBody = new
        {
            orderRef = "ORD-EMPTY",
            priority = "Normal",
            cargoType = "FinishedGood",
            requestedDeliveryDate = (DateTime?)null,
            items = Array.Empty<object>()
        };

        var updateResp = await client.PutAsJsonAsync($"/api/v1/delivery-orders/{created!.Id}", invalidBody);
        updateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateDraft_AfterSubmit_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var createBody = new
        {
            orderRef = "ORD-SUBMIT-THEN-UPDATE",
            priority = "Normal",
            cargoType = "FinishedGood",
            requestedDeliveryDate = (DateTime?)null,
            items = new[]
            {
                new
                {
                    sku = "SKU-001",
                    pickupLocationCode = pickupId.ToString(),
                    dropLocationCode = dropId.ToString(),
                    dimensions = (object?)null,
                    weightKg = 1.0,
                    quantity = new { value = 1, uom = "PCS" },
                    cargoSpecific = (object?)null
                }
            }
        };

        var createResp = await client.PostAsJsonAsync("/api/v1/delivery-orders", createBody);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<OrderDetailResponse>();

        var submitResp = await client.PostAsync($"/api/v1/delivery-orders/{created!.Id}/submit", null);
        submitResp.IsSuccessStatusCode.Should().BeTrue();

        var updateResp = await client.PutAsJsonAsync($"/api/v1/delivery-orders/{created.Id}", UpdateOrderBody());
        updateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── response shape ────────────────────────────────────────────────────────

    private record OrderDetailResponse(Guid Id, string OrderRef, string Priority, string CargoType,
        string OrderStatus, List<ItemResponse> Items);

    private record ItemResponse(Guid Id, string Sku);
}
