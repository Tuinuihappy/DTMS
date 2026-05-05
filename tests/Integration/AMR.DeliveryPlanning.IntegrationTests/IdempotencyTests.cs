using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #3: Idempotency on POST /api/delivery-orders.
/// Same Idempotency-Key → same orderId (cached 24h). Different key → new orderId.
/// </summary>
public class IdempotencyTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public IdempotencyTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SubmitOrder_SameIdempotencyKey_ReturnsSameOrderId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";

        // First call — creates the order and caches the result
        var firstResp = await SubmitWithKeyAsync(client, pickupId, dropId, idempotencyKey);
        (firstResp.StatusCode == HttpStatusCode.Created || firstResp.StatusCode == HttpStatusCode.OK)
            .Should().BeTrue("first submission should succeed");
        var firstId = await firstResp.Content.ReadFromJsonAsync<Guid>();
        firstId.Should().NotBe(Guid.Empty);

        // Second call with the SAME key — must return cached orderId, not create a new one
        var secondResp = await SubmitWithKeyAsync(client, pickupId, dropId, idempotencyKey);
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "duplicate with same idempotency key must return 200 from cache");
        var secondId = await secondResp.Content.ReadFromJsonAsync<Guid>();

        secondId.Should().Be(firstId, "same idempotency key must return the same orderId");
    }

    [Fact]
    public async Task SubmitOrder_DifferentIdempotencyKey_ReturnsDifferentOrderId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var firstResp = await SubmitWithKeyAsync(client, pickupId, dropId, $"idem-A-{Guid.NewGuid():N}");
        firstResp.IsSuccessStatusCode.Should().BeTrue();
        var firstId = await firstResp.Content.ReadFromJsonAsync<Guid>();

        var secondResp = await SubmitWithKeyAsync(client, pickupId, dropId, $"idem-B-{Guid.NewGuid():N}");
        secondResp.IsSuccessStatusCode.Should().BeTrue();
        var secondId = await secondResp.Content.ReadFromJsonAsync<Guid>();

        secondId.Should().NotBe(firstId, "different idempotency keys must produce distinct orders");
    }

    [Fact]
    public async Task SubmitOrder_WithoutIdempotencyKey_AlwaysCreatesNewOrder()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var firstResp = await SubmitWithKeyAsync(client, pickupId, dropId, idempotencyKey: null);
        var secondResp = await SubmitWithKeyAsync(client, pickupId, dropId, idempotencyKey: null);

        firstResp.IsSuccessStatusCode.Should().BeTrue();
        secondResp.IsSuccessStatusCode.Should().BeTrue();

        var firstId = await firstResp.Content.ReadFromJsonAsync<Guid>();
        var secondId = await secondResp.Content.ReadFromJsonAsync<Guid>();

        secondId.Should().NotBe(firstId, "no idempotency key → each call creates a new order");
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> SubmitWithKeyAsync(
        HttpClient client, Guid pickupId, Guid dropId, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/delivery-orders")
        {
            Content = JsonContent.Create(new
            {
                OrderId = 5001,
            OrderNo = $"IDEM-{Guid.NewGuid():N}",
            CreateBy = "test-user",
                PickupLocationCode = pickupId.ToString(),
                DropLocationCode = dropId.ToString(),
                Priority = 0,
                SLA = DateTime.UtcNow.AddHours(4),
                OrderItems = new[] { new { ItemCode = "X", Quantity = 1, Weight = 1.0, Remarks = (string?)null } }
            })
        };

        if (idempotencyKey != null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await client.SendAsync(request);
    }
}
