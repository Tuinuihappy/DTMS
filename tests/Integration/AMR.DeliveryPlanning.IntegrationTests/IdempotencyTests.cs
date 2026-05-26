using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #3: Idempotency on POST /api/v1/delivery-orders.
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
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";

        // Body must be byte-identical across calls — IdempotencyKeyFilter hashes
        // the deserialized command and rejects the second call as a conflict (422)
        // if anything differs (BuildOrderRequest is randomized per call).
        var body = DtmsWebApplicationFactory.BuildOrderRequest(pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddHours(4));

        var firstResp = await PostWithKeyAsync(client, body, idempotencyKey);
        (firstResp.StatusCode == HttpStatusCode.Created || firstResp.StatusCode == HttpStatusCode.OK)
            .Should().BeTrue($"first submission should succeed: {await firstResp.Content.ReadAsStringAsync()}");
        var firstId = await DtmsWebApplicationFactory.ReadOrderIdAsync(firstResp);
        firstId.Should().NotBe(Guid.Empty);

        var secondResp = await PostWithKeyAsync(client, body, idempotencyKey);
        secondResp.IsSuccessStatusCode.Should().BeTrue(
            $"duplicate with same idempotency key must replay the cached response: {await secondResp.Content.ReadAsStringAsync()}");
        var secondId = await DtmsWebApplicationFactory.ReadOrderIdAsync(secondResp);

        secondId.Should().Be(firstId, "same idempotency key must return the same orderId");
    }

    [Fact]
    public async Task SubmitOrder_DifferentIdempotencyKey_ReturnsDifferentOrderId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var firstResp = await SubmitWithKeyAsync(client, pickupId, dropId, profileCode, $"idem-A-{Guid.NewGuid():N}");
        firstResp.IsSuccessStatusCode.Should().BeTrue();
        var firstId = await DtmsWebApplicationFactory.ReadOrderIdAsync(firstResp);

        var secondResp = await SubmitWithKeyAsync(client, pickupId, dropId, profileCode, $"idem-B-{Guid.NewGuid():N}");
        secondResp.IsSuccessStatusCode.Should().BeTrue();
        var secondId = await DtmsWebApplicationFactory.ReadOrderIdAsync(secondResp);

        secondId.Should().NotBe(firstId, "different idempotency keys must produce distinct orders");
    }

    [Fact]
    public async Task SubmitOrder_WithoutIdempotencyKey_AlwaysCreatesNewOrder()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var firstResp = await SubmitWithKeyAsync(client, pickupId, dropId, profileCode, idempotencyKey: null);
        var secondResp = await SubmitWithKeyAsync(client, pickupId, dropId, profileCode, idempotencyKey: null);

        firstResp.IsSuccessStatusCode.Should().BeTrue();
        secondResp.IsSuccessStatusCode.Should().BeTrue();

        var firstId = await DtmsWebApplicationFactory.ReadOrderIdAsync(firstResp);
        var secondId = await DtmsWebApplicationFactory.ReadOrderIdAsync(secondResp);

        secondId.Should().NotBe(firstId, "no idempotency key → each call creates a new order");
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> SubmitWithKeyAsync(
        HttpClient client, Guid pickupId, Guid dropId, string profileCode, string? idempotencyKey) =>
        PostWithKeyAsync(client,
            DtmsWebApplicationFactory.BuildOrderRequest(pickupId, dropId, profileCode,
                sla: DateTime.UtcNow.AddHours(4)),
            idempotencyKey);

    private static async Task<HttpResponseMessage> PostWithKeyAsync(
        HttpClient client, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/delivery-orders")
        {
            Content = JsonContent.Create(body)
        };

        if (idempotencyKey != null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await client.SendAsync(request);
    }
}
