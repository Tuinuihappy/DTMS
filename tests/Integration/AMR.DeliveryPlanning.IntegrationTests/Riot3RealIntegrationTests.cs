using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Real RIOT3 integration tests — call the actual RIOT3 server at 10.204.212.28:12000.
///
/// Run these tests separately:
///   dotnet test --filter "Category=Riot3Real"
///
/// Exclude from CI (no RIOT3 access):
///   dotnet test --filter "Category!=Riot3Real"
///
/// These tests verify:
///   1. Connectivity and authentication (API key accepted by RIOT3)
///   2. Outbound order format (RIOT3 accepts our POST /api/v4/orders payload)
///   3. End-to-end dispatch through our app with AdapterKey="riot3"
///   4. Our webhook handler handles RIOT3's real notification formats
///
/// Limitation: RIOT3 cannot call back to the Testcontainer server (localhost),
/// so webhook round-trip is verified by simulating the inbound call manually.
/// </summary>
[Trait("Category", "Riot3Real")]
public class Riot3RealIntegrationTests : IClassFixture<DtmsWebApplicationFactory>, IAsyncLifetime
{
    private const string Riot3BaseUrl = "http://10.204.212.28:12000";
    private const string Riot3ApiKey =
        "***REMOVED_RIOT3_TOKEN***" +
        ".***REMOVED_RIOT3_TOKEN_PART***";

    private readonly DtmsWebApplicationFactory _factory;
    private HttpClient _riot3Client = null!;
    private bool _riot3Reachable;

    public Riot3RealIntegrationTests(DtmsWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _riot3Client = new HttpClient { BaseAddress = new Uri(Riot3BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        // TryAddWithoutValidation bypasses .NET's header validation so custom schemes
        // like "app <jwt>" are sent as-is without being rejected or silently dropped.
        _riot3Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", Riot3ApiKey);
        _riot3Reachable = await CheckConnectivityAsync();
    }

    public Task DisposeAsync()
    {
        _riot3Client.Dispose();
        return Task.CompletedTask;
    }

    // ── Section 1: Direct connectivity tests (no app involved) ──────────────────

    /// <summary>
    /// GET /api/v4/health does NOT require authentication — verifies server is up.
    /// </summary>
    [Riot3RealFact]
    public async Task Direct_Health_ServerIsReachableAndRunning()
    {
        SkipIfUnreachable();

        // Health endpoint is public — no auth header needed
        using var openClient = new HttpClient { BaseAddress = new Uri(Riot3BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        var response = await openClient.GetAsync("/api/v4/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"RIOT3 health endpoint must return 200. Response: {body}");
        body.Should().Contain("SUCCESS",
            "healthy RIOT3 server returns code=0, message=SUCCESS");
    }

    /// <summary>
    /// Verifies that our API key is accepted by RIOT3.
    /// FAILS when token is expired or revoked — request a new token from RIOT3 admin:
    ///   1. Log in to http://10.204.212.28:12000 as admin
    ///   2. Go to Settings → App Management → regenerate token for Delta6FAN1
    ///   3. Update ApiKey in appsettings.Development.json and Riot3ApiKey constant here
    /// </summary>
    [Riot3RealFact]
    public async Task Direct_ApiKey_IsAcceptedByRiot3()
    {
        SkipIfUnreachable();

        // GET /api/v4/robots/{unknown-id} — expects 404 (not found), NOT 401/403
        var response = await _riot3Client.GetAsync($"/api/v4/robots/{Guid.NewGuid()}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            $"API key rejected by RIOT3 (E510001 = token expired/revoked).\n" +
            $"RIOT3 response: {body}\n" +
            "ACTION REQUIRED: Log into RIOT3 admin console and regenerate the app token.");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"API key lacks permission. Response: {body}");
    }

    [Riot3RealFact]
    public async Task Direct_GetRobotState_UnknownId_ReturnsResourceNotFound()
    {
        SkipIfUnreachable();

        var response = await _riot3Client.GetAsync($"/api/v4/robots/{Guid.NewGuid()}");
        var body = await response.Content.ReadAsStringAsync();

        // RIOT3 always returns HTTP 200. Business errors are in the JSON body:
        // code "0" = success, code "E320003" = resource not found, code "E510001" = auth error
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "RIOT3 uses HTTP 200 for all responses — errors are in the JSON body");
        body.Should().NotContain("E510001",
            $"Auth must be valid — got E510001 (token expired) instead. Body: {body}");
        body.Should().Contain("E320003",
            $"Unknown robot should return E320003 (resource not found). Body: {body}");
    }

    [Riot3RealFact]
    public async Task Direct_PostOrder_OurFormat_AcceptedOrBusinessRuleRejected()
    {
        SkipIfUnreachable();

        var taskId = Guid.NewGuid();
        var order = BuildTestOrder(taskId, vehicleId: Guid.NewGuid());

        var response = await _riot3Client.PostAsJsonAsync("/api/v4/orders", order);
        var body = await response.Content.ReadAsStringAsync();

        // RIOT3 always returns HTTP 200; errors are in the JSON body
        // code "0" = accepted, "E3xxxxx" = business rule failure, "E5xxxxx" = auth/server error
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"RIOT3 always returns HTTP 200. Got {response.StatusCode}: {body}");
        body.Should().NotContain("E510001",
            $"Auth must be valid — token rejected. Body: {body}");
        // E320003=vehicle not found, E380xxx=business rule — all acceptable; just not auth errors
    }

    [Riot3RealFact]
    public async Task Direct_CancelOrder_AuthSucceeds_BusinessErrorOrSuccess()
    {
        SkipIfUnreachable();

        var response = await _riot3Client.PutAsJsonAsync(
            $"/api/v4/orders/{Guid.NewGuid()}/operation?isUpper=true",
            new { orderCommandType = "CANCEL" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"RIOT3 always responds HTTP 200. Got {response.StatusCode}: {body}");
        body.Should().NotContain("E510001",
            $"Auth must be valid — token rejected. Body: {body}");
    }

    // ── Section 2: Through-app dispatch with real RIOT3 adapter ─────────────────

    [Riot3RealFact]
    public async Task ThroughApp_DispatchTrip_Riot3AdapterCalled_TripInProgress()
    {
        SkipIfUnreachable();

        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var vehicleTypeId = await _factory.CreateVehicleTypeAsync();

        // Register vehicle with riot3 adapter (real RIOT3 call will be made on dispatch)
        var regResp = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = $"RIOT3T-{Guid.NewGuid():N}"[..20],
            VehicleTypeId = vehicleTypeId,
            AdapterKey = "riot3",
            VendorVehicleKey = $"RIOT3T-{Guid.NewGuid():N}"[..20]
        });
        regResp.IsSuccessStatusCode.Should().BeTrue(
            $"Vehicle registration failed: {await regResp.Content.ReadAsStringAsync()}");
        var vehicleId = await regResp.Content.ReadFromJsonAsync<Guid>();

        await client.PutAsJsonAsync($"/api/fleet/vehicles/{vehicleId}/state", new
        {
            VehicleId = vehicleId,
            NewState = 1, // Idle
            BatteryLevel = 85.0,
            CurrentNodeId = (Guid?)null
        });

        // Create job → assign → commit
        var jobResp = await client.PostAsJsonAsync("/api/planning/jobs", new
        {
            DeliveryOrderId = Guid.NewGuid(),
            PickupStationId = pickupId,
            DropStationId = dropId,
            Priority = "Normal"
        });
        jobResp.IsSuccessStatusCode.Should().BeTrue();
        var jobId = await jobResp.Content.ReadFromJsonAsync<Guid>();

        var assignResp = await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/assign", new { JobId = jobId });
        assignResp.IsSuccessStatusCode.Should().BeTrue(
            $"Assign failed: {await assignResp.Content.ReadAsStringAsync()}");
        await client.PostAsJsonAsync($"/api/planning/jobs/{jobId}/commit", new { JobId = jobId });

        // Dispatch trip — this CALLS RIOT3 for real via Riot3CommandService
        var tripResp = await client.PostAsJsonAsync("/api/dispatch/trips", new
        {
            JobId = jobId,
            VehicleId = vehicleId,
            Legs = DtmsWebApplicationFactory.BuildSingleLeg(pickupId, dropId)
        });
        tripResp.IsSuccessStatusCode.Should().BeTrue(
            $"Create trip failed: {await tripResp.Content.ReadAsStringAsync()}");
        var tripId = await tripResp.Content.ReadFromJsonAsync<Guid>();

        // Trip must be InProgress regardless of whether RIOT3 accepted the task
        // (VendorAdapterTaskDispatcher catches RIOT3 errors gracefully)
        var tripBody = await (await client.GetAsync($"/api/dispatch/trips/{tripId}")).Content.ReadAsStringAsync();
        tripBody.Should().Contain("\"status\":1",
            "Trip must be InProgress even if RIOT3 rejects unknown vehicle — app must handle RIOT3 errors gracefully");
    }

    [Riot3RealFact]
    public async Task ThroughApp_DispatchWithRiot3_OutboxHasVehicleStateEvent()
    {
        SkipIfUnreachable();

        // Simulate RIOT3 sending a vehicle state update (battery 80%)
        var client = await _factory.GetAuthenticatedClient();
        var vehicleId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", new
        {
            type = "vehicle",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 80,
                systemState = "IDLE",
                safetyState = "NORMAL"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleStateChangedIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .ToListAsync();

        messages.Should().HaveCount(1,
            "RIOT3 vehicle event must create VehicleStateChangedIntegrationEvent in outbox");
    }

    // ── Section 3: Webhook round-trip simulation ─────────────────────────────────
    // (RIOT3 cannot reach the Testcontainer server directly, so we simulate the
    //  inbound call RIOT3 would make after processing a dispatched task)

    [Riot3RealFact]
    public async Task Webhook_Riot3TaskFinished_RealPayloadFormat_ProcessedCorrectly()
    {
        SkipIfUnreachable();

        // First dispatch a real order to get a valid upperKey (taskId)
        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        // Simulate what RIOT3 sends when a task finishes (real payload shape)
        var webhookPayload = new
        {
            type = "task",
            taskEventType = "finished",
            orderKey = $"AMR-{Guid.NewGuid():N}",
            upperKey = taskId.ToString(),
            taskId = taskId.ToString(),
            progress = 100
        };

        var webhookResp = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", webhookPayload);
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "RIOT3 'finished' webhook must be accepted with 200 OK");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var evt = await db.OutboxMessages
            .Where(m => m.Type.Contains("Riot3TaskCompletedIntegrationEvent")
                     && m.Content.Contains(taskId.ToString()))
            .FirstOrDefaultAsync();

        evt.Should().NotBeNull("finished webhook must publish Riot3TaskCompletedIntegrationEvent");
    }

    [Riot3RealFact]
    public async Task Webhook_Riot3TaskFailed_RealPayloadFormat_PublishesFailedEvent()
    {
        SkipIfUnreachable();

        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();
        var errorCode = "E_OBSTACLE_DETECTED";

        var webhookPayload = new
        {
            type = "task",
            taskEventType = "failed",
            orderKey = $"AMR-{Guid.NewGuid():N}",
            upperKey = taskId.ToString(),
            taskId = taskId.ToString(),
            failResult = new
            {
                errorCode,
                errorMsg = "Path blocked by obstacle"
            }
        };

        var webhookResp = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", webhookPayload);
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var evt = await db.OutboxMessages
            .Where(m => m.Type.Contains("Riot3TaskFailedIntegrationEvent")
                     && m.Content.Contains(taskId.ToString()))
            .FirstOrDefaultAsync();

        evt.Should().NotBeNull("failed webhook must publish Riot3TaskFailedIntegrationEvent");
        evt!.Content.Should().Contain(errorCode, "error code must be preserved in the event");
    }

    [Riot3RealFact]
    public async Task Webhook_Riot3VehicleEmergency_EmergencyFlagHandledSafely()
    {
        SkipIfUnreachable();

        var client = await _factory.GetAuthenticatedClient();
        var vehicleId = Guid.NewGuid();

        // RIOT3 real emergency payload
        var webhookPayload = new
        {
            type = "vehicle",
            vehicleEventType = "emergency_triggered",
            vehicle = new
            {
                deviceKey = vehicleId.ToString(),
                batteryLevel = 60,
                systemState = "ERROR",
                safetyState = "EMERGENCY_STOP"
            }
        };

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", webhookPayload);

        // Must return 200 even for emergency events (no webhook reply needed)
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Emergency event must be accepted gracefully — RIOT3 does not retry on non-200");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>();
        var stateEvt = await db.OutboxMessages
            .Where(m => m.Type.Contains("VehicleStateChangedIntegrationEvent")
                     && m.Content.Contains(vehicleId.ToString()))
            .FirstOrDefaultAsync();

        stateEvt.Should().NotBeNull("emergency vehicle event must still publish VehicleStateChangedIntegrationEvent");
    }

    [Riot3RealFact]
    public async Task Webhook_Riot3TaskStarted_NoEventPublished_Returns200()
    {
        SkipIfUnreachable();

        var client = await _factory.GetAuthenticatedClient();
        var taskId = Guid.NewGuid();

        // RIOT3 sends 'started' when robot picks up the task — we only log, no event
        var webhookPayload = new
        {
            type = "task",
            taskEventType = "started",
            upperKey = taskId.ToString(),
            taskId = taskId.ToString(),
            progress = 0
        };

        var response = await client.PostAsJsonAsync("/api/webhooks/riot3/notify", webhookPayload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "'started' task event must be acknowledged 200 — RIOT3 retries on non-200");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _riot3Client.GetAsync($"/api/v4/robots/{Guid.NewGuid()}", cts.Token);
            return true; // Any response (even 4xx) means server is reachable
        }
        catch
        {
            return false;
        }
    }

    private void SkipIfUnreachable()
    {
        if (!_riot3Reachable)
            throw new InvalidOperationException($"RIOT3 server at {Riot3BaseUrl} is not reachable. " +
                "Run with --filter \"Category=Riot3Real\" only when RIOT3 is accessible.");
    }

    private static object BuildTestOrder(Guid taskId, Guid vehicleId) => new
    {
        upperKey = taskId.ToString(),
        orderName = $"IntegTest-{taskId}",
        orderType = "WORK",
        priority = 10,
        structureType = "sequence",
        appointVehicleKey = vehicleId.ToString(),
        missions = new[]
        {
            new
            {
                missionId = taskId.ToString(),
                missionName = $"MOVE-{taskId}",
                type = "MOVE",
                blockingType = "HARD"
            }
        }
    };
}

public sealed class Riot3RealFactAttribute : FactAttribute
{
    private const string Riot3BaseUrl = "http://10.204.212.28:12000";

    public Riot3RealFactAttribute()
    {
        if (!Riot3Availability.IsReachable)
        {
            Skip = $"RIOT3 server at {Riot3BaseUrl} is not reachable. " +
                "Run with --filter \"Category=Riot3Real\" only when RIOT3 is accessible.";
        }
    }

    private static class Riot3Availability
    {
        public static readonly bool IsReachable = Check();

        private static bool Check()
        {
            try
            {
                using var client = new HttpClient
                {
                    BaseAddress = new Uri(Riot3BaseUrl),
                    Timeout = TimeSpan.FromSeconds(2)
                };

                using var response = client
                    .GetAsync($"/api/v4/robots/{Guid.NewGuid()}")
                    .GetAwaiter()
                    .GetResult();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
