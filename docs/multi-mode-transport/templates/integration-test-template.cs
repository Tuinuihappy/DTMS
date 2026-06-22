// =============================================================================
// INTEGRATION TEST TEMPLATE
// =============================================================================
//
// Stack (per ADR-004):
//   - xUnit + FluentAssertions (same as unit tests)
//   - DtmsWebApplicationFactory (real HTTP + DI + DB)
//   - Testcontainers Postgres / RabbitMQ / Redis (real infrastructure)
//
// Folder layout:
//   tests/Modules/{Module}.IntegrationTests/         ← per-module integration
//   OR
//   tests/Integration/AMR.DeliveryPlanning.IntegrationTests/  ← cross-module e2e
//
// Reference examples:
//   tests/Integration/.../Riot3WebhookTests.cs              (webhook → outbox flow)
//   tests/Integration/.../EndToEndPipelineTests.cs          (full lifecycle)
//   tests/Integration/.../CapabilityAssignmentTests.cs      (API + DB roundtrip)
//   tests/Integration/.../AuthHelper.cs                     (auth setup helper)
//
// What goes here (vs unit tests):
//   ✓ HTTP request → DB write → integration event in outbox
//   ✓ Webhook → consumer → state mutation
//   ✓ Migration applies cleanly to fresh DB
//   ✓ Cross-module event flow (DeliveryOrder → Planning → Dispatch)
//   ✓ Real query patterns (joins, indexes, projection updates)
//   ✗ Don't put pure domain logic tests here (use unit tests — faster)
//   ✗ Don't test third-party libraries (assume they work)
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using AMR.DeliveryPlanning.{Module}.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// {Phase reference} — {Test category} for {Feature under test}.
///
/// Verifies: {bullet what's verified — e.g. webhook routing, idempotency,
///            cross-module event propagation, projection updates}
/// </summary>
public class {Feature}IntegrationTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public {Feature}IntegrationTests(DtmsWebApplicationFactory factory) => _factory = factory;

    // ─── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task {Endpoint}_{Scenario}_{Outcome}()
    {
        // Arrange — obtain authenticated client (operator or dispatcher per endpoint)
        var client = await _factory.GetAuthenticatedClient();
        var someId = Guid.NewGuid();

        // Act — call the API
        var response = await client.PostAsJsonAsync("/api/{path}", new
        {
            field1 = "value",
            field2 = someId,
            // ... request body
        });

        // Assert — HTTP status
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — body shape (if applicable)
        var body = await response.Content.ReadFromJsonAsync<{ResponseDto}>();
        body.Should().NotBeNull();
        body!.{Field}.Should().Be({expectedValue});

        // Assert — DB side-effect
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<{DbContext}>();
        var entity = await db.{DbSet}.SingleOrDefaultAsync(e => e.Id == someId);
        entity.Should().NotBeNull();
        entity!.{Field}.Should().Be({expectedValue});

        // Assert — integration event in outbox (if applicable)
        var outboxMessages = await db.OutboxMessages
            .Where(m => m.Type.Contains("{ExpectedEventName}")
                     && m.Content.Contains(someId.ToString()))
            .ToListAsync();

        outboxMessages.Should().HaveCount(1, "exactly one event should be emitted");
        outboxMessages[0].ProcessedOnUtc.Should().BeNull("event not yet processed by consumer");
        outboxMessages[0].Error.Should().BeNull();
    }

    // ─── Failure paths ────────────────────────────────────────────────────

    [Fact]
    public async Task {Endpoint}_WhenInvalidInput_Returns400()
    {
        var client = await _factory.GetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/{path}", new
        {
            field1 = "",     // invalid (empty)
            field2 = Guid.Empty
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("field1");
    }

    [Fact]
    public async Task {Endpoint}_WhenUnauthenticated_Returns401()
    {
        // Use the factory's anonymous client (no JWT)
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/{path}", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task {Endpoint}_WhenWrongAudience_Returns403()
    {
        // E.g. operator app JWT calling dispatcher endpoint (per ADR-007)
        var client = await _factory.GetAuthenticatedClient(audience: "operator-app");

        var response = await client.PostAsJsonAsync("/api/dispatcher/{path}", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Idempotency ──────────────────────────────────────────────────────

    [Fact]
    public async Task {Endpoint}_SameRequestTwice_HasSameEffectAsOnce()
    {
        var client = await _factory.GetAuthenticatedClient();
        var someId = Guid.NewGuid();
        var requestBody = new { id = someId, field = "value" };

        // Send twice
        var response1 = await client.PostAsJsonAsync("/api/{path}", requestBody);
        var response2 = await client.PostAsJsonAsync("/api/{path}", requestBody);

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);   // or 409 if you reject duplicates

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<{DbContext}>();
        var count = await db.{DbSet}.CountAsync(e => e.Id == someId);
        count.Should().Be(1, "duplicate requests must not create multiple rows");
    }

    // ─── End-to-end cross-module flow ─────────────────────────────────────

    [Fact]
    public async Task Order_Create_DispatchesTrip_PublishesEvents()
    {
        var client = await _factory.GetAuthenticatedClient();

        // 1. Create order
        var orderResp = await client.PostAsJsonAsync("/api/delivery-orders", new
        {
            transportMode = "Manual",
            pickupWarehouseId = await SeedWarehouseAsync(),
            dropWarehouseId = await SeedWarehouseAsync(),
            items = new[] { new { weightKg = 10 } }
        });
        orderResp.EnsureSuccessStatusCode();
        var order = await orderResp.Content.ReadFromJsonAsync<{OrderDto}>();

        // 2. Trigger dispatch (might be auto via consumer; otherwise call explicitly)
        var dispatchResp = await client.PostAsJsonAsync($"/api/orders/{order!.Id}/dispatch", new { });
        dispatchResp.EnsureSuccessStatusCode();

        // 3. Wait for outbox to process (consumer-driven)
        await WaitForCondition(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
            return await db.Trips.AnyAsync(t => t.DeliveryOrderId == order.Id);
        }, timeout: TimeSpan.FromSeconds(30));

        // 4. Verify Trip created with correct mode
        using var scope = _factory.Services.CreateScope();
        var dispatch = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
        var trip = await dispatch.Trips.SingleAsync(t => t.DeliveryOrderId == order.Id);
        trip.TransportMode.Should().Be(TransportMode.Manual);
        trip.Status.Should().Be(TripStatus.Created);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Poll a condition until satisfied or timeout. Use for eventual-consistency
    /// assertions (outbox processing, projection updates).
    /// </summary>
    private static async Task WaitForCondition(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(interval);
        }

        throw new TimeoutException($"Condition not met within {timeout}");
    }

    /// <summary>
    /// Seed a Warehouse for tests that need a pickup/drop reference.
    /// Returns the created Warehouse Id.
    /// </summary>
    private async Task<Guid> SeedWarehouseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var facilityDb = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();

        var warehouse = Warehouse.Create(
            code: $"WH-TEST-{Guid.NewGuid():N}",
            name: "Test Warehouse",
            location: new LatLng(13.7, 100.5));

        facilityDb.Warehouses.Add(warehouse);
        await facilityDb.SaveChangesAsync();
        return warehouse.Id;
    }
}


// =============================================================================
// AUTH HELPER (place in shared file: AuthHelper.cs)
// =============================================================================
//
// Extend DtmsWebApplicationFactory with helpers per audience:
//
//   public static class DtmsAuthHelpers
//   {
//       public static async Task<HttpClient> GetAuthenticatedClient(
//           this DtmsWebApplicationFactory factory,
//           string audience = "dispatcher-console")
//       {
//           var client = factory.CreateClient();
//           var token = await IssueTestJwt(factory, audience);
//           client.DefaultRequestHeaders.Authorization =
//               new AuthenticationHeaderValue("Bearer", token);
//           return client;
//       }
//
//       private static async Task<string> IssueTestJwt(
//           DtmsWebApplicationFactory factory,
//           string audience)
//       {
//           // Use test signing key registered in WebApplicationFactory
//           // Build JWT with claims: aud, sub, ...
//       }
//   }
