using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Phase 3 — Test #8: PATCH order creates amendment record; GET /timeline includes it.
/// </summary>
public class AmendmentTimelineTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public AmendmentTimelineTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PatchOrder_ChangePriority_TimelineIncludesAmendmentEntry()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, priority: 0); // Low

        // PATCH: change priority to High (2)
        var patchResp = await client.PatchAsJsonAsync($"/api/delivery-orders/{orderId}", new
        {
            Reason = "Customer escalation",
            NewPriority = 2,   // High
            AmendedBy = "ops-user-1"
        });
        patchResp.IsSuccessStatusCode.Should().BeTrue(
            $"PATCH order failed: {await patchResp.Content.ReadAsStringAsync()}");

        // GET timeline
        var timelineResp = await client.GetAsync($"/api/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var timelineBody = await timelineResp.Content.ReadAsStringAsync();

        // Timeline must include an Amendment entry
        timelineBody.Should().Contain("Amendment:PriorityChange",
            "changing priority must create an Amendment:PriorityChange timeline entry");
        timelineBody.Should().Contain("Customer escalation",
            "amendment reason must appear in timeline");
    }

    [Fact]
    public async Task PatchOrder_ChangeSla_TimelineIncludesSlaAmendment()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId);
        var newSla = DateTime.UtcNow.AddHours(8);

        var patchResp = await client.PatchAsJsonAsync($"/api/delivery-orders/{orderId}", new
        {
            Reason = "Production delay — extended SLA",
            NewSla = newSla,
            AmendedBy = "planner-1"
        });
        patchResp.IsSuccessStatusCode.Should().BeTrue(
            $"PATCH SLA failed: {await patchResp.Content.ReadAsStringAsync()}");

        var timelineResp = await client.GetAsync($"/api/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await timelineResp.Content.ReadAsStringAsync();

        body.Should().Contain("Amendment:SlaChange",
            "changing SLA must create an Amendment:SlaChange timeline entry");
    }

    [Fact]
    public async Task PatchOrder_MultipleAmendments_TimelineOrderedChronologically()
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);

        var orderId = await SubmitOrderAsync(client, pickupId, dropId, priority: 0);

        // First amendment: change priority
        await client.PatchAsJsonAsync($"/api/delivery-orders/{orderId}", new
        {
            Reason = "First change",
            NewPriority = 1, // Normal
            AmendedBy = "user-A"
        });

        await Task.Delay(50); // ensure distinct timestamps

        // Second amendment: change SLA
        await client.PatchAsJsonAsync($"/api/delivery-orders/{orderId}", new
        {
            Reason = "Second change",
            NewSla = DateTime.UtcNow.AddHours(6),
            AmendedBy = "user-B"
        });

        var timelineResp = await client.GetAsync($"/api/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await timelineResp.Content.ReadAsStringAsync();

        // Both amendments must appear
        body.Should().Contain("First change");
        body.Should().Contain("Second change");

        // Verify chronological order: "First change" must appear before "Second change"
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

        var orderId = await SubmitOrderAsync(client, pickupId, dropId);

        var timelineResp = await client.GetAsync($"/api/delivery-orders/{orderId}/timeline");
        timelineResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await timelineResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "timeline must have at least one audit entry after order creation");
    }

    [Fact]
    public async Task GetTimeline_NonExistentOrder_Returns404()
    {
        var client = await _factory.GetAuthenticatedClient();

        var resp = await client.GetAsync($"/api/delivery-orders/{Guid.NewGuid()}/timeline");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private static async Task<Guid> SubmitOrderAsync(
        HttpClient client, Guid pickupId, Guid dropId, int priority = 1)
    {
        var resp = await client.PostAsJsonAsync("/api/delivery-orders", new
        {
            OrderKey = $"AMD-{Guid.NewGuid():N}",
            PickupLocationCode = pickupId.ToString(),
            DropLocationCode = dropId.ToString(),
            Priority = priority,
            SLA = DateTime.UtcNow.AddHours(4),
            Lines = new[] { new { ItemCode = "ITEM", Quantity = 1, Weight = 1.0, Remarks = (string?)null } }
        });
        resp.IsSuccessStatusCode.Should().BeTrue($"Submit order failed: {await resp.Content.ReadAsStringAsync()}");
        return await resp.Content.ReadFromJsonAsync<Guid>();
    }
}
