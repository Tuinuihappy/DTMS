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
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddMinutes(10));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SLA less than 30 minutes in future must be rejected with 400");
    }

    [Fact]
    public async Task SubmitOrder_SlaExactly29Min_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddMinutes(29));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SLA of 29 minutes is still below the 30-minute minimum");
    }

    [Fact]
    public async Task SubmitOrder_SlaInThePast_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddHours(-1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SLA in the past must be rejected");
    }

    [Fact]
    public async Task SubmitOrder_ValidSla4Hours_ReturnsOrderId()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId, profileCode,
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
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var response = await SubmitOrderAsync(client, pickupId, dropId, profileCode, sla: null);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"Order without SLA must be accepted: {await response.Content.ReadAsStringAsync()}");
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> SubmitOrderAsync(
        HttpClient client, Guid pickupId, Guid dropId, string profileCode, DateTime? sla)
        => client.PostAsJsonAsync("/api/v1/delivery-orders",
            DtmsWebApplicationFactory.BuildOrderRequest(pickupId, dropId, profileCode, sla));
}
