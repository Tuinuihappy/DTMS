using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #4: SLA validation on order submission.
/// SLA &lt; 30 min in future → 400 Bad Request. Valid SLA → 201 Created.
/// </summary>
public class SlaValidationTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public SlaValidationTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SubmitOrder_SlaLessThan30Min_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        // SLA only 10 minutes in the future — must be rejected
        var response = await SubmitOrderAsync(client, pickupId, dropId,
            sla: DateTime.UtcNow.AddMinutes(10));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SLA less than 30 minutes in future must be rejected with 400");
    }

    [Fact]
    public async Task SubmitOrder_SlaExactly29Min_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId,
            sla: DateTime.UtcNow.AddMinutes(29));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SLA of 29 minutes is still below the 30-minute minimum");
    }

    [Fact]
    public async Task SubmitOrder_SlaInThePast_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId,
            sla: DateTime.UtcNow.AddHours(-1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SLA in the past must be rejected");
    }

    [Fact]
    public async Task SubmitOrder_ValidSla4Hours_ReturnsOrderId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId,
            sla: DateTime.UtcNow.AddHours(4));

        response.IsSuccessStatusCode.Should().BeTrue(
            $"SLA of 4 hours is valid: {await response.Content.ReadAsStringAsync()}");
        var orderId = await response.Content.ReadFromJsonAsync<Guid>();
        orderId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SubmitOrder_NoSla_Succeeds()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        // SLA is optional — null means no deadline constraint
        var response = await SubmitOrderAsync(client, pickupId, dropId, sla: null);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"Order without SLA must be accepted: {await response.Content.ReadAsStringAsync()}");
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> SubmitOrderAsync(
        HttpClient client, Guid pickupId, Guid dropId, DateTime? sla)
    {
        return await client.PostAsJsonAsync("/api/delivery-orders", new
        {
            OrderId = 4001,
            OrderNo = $"SLA-{Guid.NewGuid():N}",
            CreateBy = "test-user",
            PickupLocationCode = pickupId.ToString(),
            DropLocationCode = dropId.ToString(),
            Priority = 0,
            SLA = sla,
            OrderItems = new[] { new { ItemCode = "ITEM", Quantity = 1, Weight = 1.0, Remarks = (string?)null } }
        });
    }
}
