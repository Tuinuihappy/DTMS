using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DTMS.IntegrationTests;

/// <summary>
/// Phase 3 — Test #8: PATCH order creates amendment record; GET /timeline includes it.
/// </summary>
public class AmendmentTimelineTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public AmendmentTimelineTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PatchOrder_ChangeServiceWindow_TimelineIncludesAmendmentEntry()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, profileCode);

        var patchResp = await client.PatchAsJsonAsync($"/api/v1/delivery-orders/{orderId}", new
        {
            Reason = "Customer escalation",
            NewServiceWindow = new { Earliest = (DateTime?)null, Latest = DateTime.UtcNow.AddHours(6) },
            AmendedBy = "ops-user-1"
        });
        patchResp.IsSuccessStatusCode.Should().BeTrue(
            $"PATCH order failed: {await patchResp.Content.ReadAsStringAsync()}");

        var timelineResp = await client.GetAsync($"/api/v1/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var timelineBody = await timelineResp.Content.ReadAsStringAsync();

        timelineBody.Should().Contain("Amendment:ServiceWindowChange",
            "changing service window must create an Amendment:ServiceWindowChange timeline entry");
        timelineBody.Should().Contain("Customer escalation",
            "amendment reason must appear in timeline");
    }

    [Fact]
    public async Task PatchOrder_ChangeServiceWindow_TimelineIncludesAmendment()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, profileCode);

        var patchResp = await client.PatchAsJsonAsync($"/api/v1/delivery-orders/{orderId}", new
        {
            Reason = "Production delay — extended window",
            NewServiceWindow = new { Earliest = (DateTime?)null, Latest = DateTime.UtcNow.AddHours(8) },
            AmendedBy = "planner-1"
        });
        patchResp.IsSuccessStatusCode.Should().BeTrue(
            $"PATCH service window failed: {await patchResp.Content.ReadAsStringAsync()}");

        var timelineResp = await client.GetAsync($"/api/v1/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await timelineResp.Content.ReadAsStringAsync();

        body.Should().Contain("Amendment:ServiceWindowChange",
            "changing service window must create an Amendment:ServiceWindowChange timeline entry");
    }

    [Fact]
    public async Task PatchOrder_MultipleAmendments_TimelineOrderedChronologically()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, profileCode);

        await client.PatchAsJsonAsync($"/api/v1/delivery-orders/{orderId}", new
        {
            Reason = "First change",
            NewServiceWindow = new { Earliest = (DateTime?)null, Latest = DateTime.UtcNow.AddHours(5) },
            AmendedBy = "user-A"
        });

        await Task.Delay(50);

        await client.PatchAsJsonAsync($"/api/v1/delivery-orders/{orderId}", new
        {
            Reason = "Second change",
            NewServiceWindow = new { Earliest = (DateTime?)null, Latest = DateTime.UtcNow.AddHours(6) },
            AmendedBy = "user-B"
        });

        var timelineResp = await client.GetAsync($"/api/v1/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await timelineResp.Content.ReadAsStringAsync();

        body.Should().Contain("First change");
        body.Should().Contain("Second change");

        var firstIdx = body.IndexOf("First change", StringComparison.Ordinal);
        var secondIdx = body.IndexOf("Second change", StringComparison.Ordinal);
        firstIdx.Should().BeLessThan(secondIdx,
            "timeline entries must be ordered chronologically (oldest first)");
    }

    [Fact]
    public async Task GetTimeline_AfterSubmit_ContainsInitialEntry()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, profileCode);

        var timelineResp = await client.GetAsync($"/api/v1/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await timelineResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "timeline must have at least one audit entry after order creation");
    }

    [Fact]
    public async Task GetTimeline_NonExistentOrder_ReturnsEmptyArray()
    {
        var client = await _factory.GetAuthenticatedClient();

        var resp = await client.GetAsync($"/api/v1/delivery-orders/{Guid.NewGuid()}/timeline");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(0, "non-existent order has no timeline entries");
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private Task<Guid> SubmitOrderAsync(HttpClient client, Guid pickupId, Guid dropId, string profileCode)
        => _factory.CreateAndSubmitOrderAsync(client, pickupId, dropId, profileCode,
            sla: DateTime.UtcNow.AddHours(4));
}
