using System.Net;
using System.Text.Json;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace VendorAdapter.UnitTests;

public class UnitTest1
{
    [Fact]
    public async Task SendOrderAsync_PostsEnvelopeWithUpperKey()
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var request = new Riot3OrderRequest
        {
            UpperKey = "abc123-G1",
            OrderName = "envelope-test",
            OrderType = "WORK",
            Priority = 10,
            StructureType = "sequence",
            AppointVehicleKey = "SEER-001",
            Missions = new List<Riot3Mission>
            {
                new() { MissionKey = Guid.NewGuid().ToString(), Type = "MOVE", Category = "agv", MapId = 27, StationId = 5 }
            }
        };

        var result = await service.SendOrderAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.JsonBody);

        using var doc = JsonDocument.Parse(handler.JsonBody!);
        Assert.Equal("abc123-G1", doc.RootElement.GetProperty("upperKey").GetString());
        Assert.Equal("sequence", doc.RootElement.GetProperty("structureType").GetString());
        var mission = doc.RootElement.GetProperty("missions")[0];
        Assert.Equal("MOVE", mission.GetProperty("type").GetString());
        Assert.Equal(27, mission.GetProperty("mapId").GetInt32());
        Assert.Equal(5, mission.GetProperty("stationId").GetInt32());
    }

    [Fact]
    public async Task SendOrderAsync_MissionWireShape_MatchesRiot3SpecExactly()
    {
        // Pin the wire shape against the RIOT3 spec example: nullable fields
        // must drop out so MOVE missions don't carry actionType/blockingType
        // keys and ACT missions don't carry mapId/stationId. actionName must
        // not appear at all (RIOT3 looks it up against its catalog and an
        // incidental name match would override our inline params).
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var request = new Riot3OrderRequest
        {
            UpperKey = "G1",
            OrderName = "wire-test",
            OrderType = "WORK",
            Priority = 50,
            StructureType = "sequence",
            Missions = new List<Riot3Mission>
            {
                new() { Type = "MOVE", Category = "agv", MapId = 2, StationId = 2 },
                new()
                {
                    Type = "ACT", Category = "agv",
                    ActionType = "standardRobotsCustom",
                    BlockingType = "NONE",
                    ActionParameters = new List<Riot3ActionParam>
                    {
                        new() { Key = "id", Value = 131 },
                        new() { Key = "param0", Value = 0 },
                        new() { Key = "param1", Value = 0 }
                    }
                }
            }
        };

        await service.SendOrderAsync(request);

        Assert.NotNull(handler.JsonBody);
        using var doc = JsonDocument.Parse(handler.JsonBody!);

        var move = doc.RootElement.GetProperty("missions")[0];
        Assert.Equal("MOVE", move.GetProperty("type").GetString());
        Assert.Equal(2, move.GetProperty("mapId").GetInt32());
        Assert.Equal(2, move.GetProperty("stationId").GetInt32());
        Assert.False(move.TryGetProperty("actionType", out _), "MOVE must not include actionType");
        Assert.False(move.TryGetProperty("blockingType", out _), "MOVE must not include blockingType");
        Assert.False(move.TryGetProperty("actionParameters", out _), "MOVE must not include actionParameters");
        Assert.False(move.TryGetProperty("actionName", out _), "wire must never include actionName");
        Assert.False(move.TryGetProperty("missionIndex", out _), "spec example omits missionIndex");

        var act = doc.RootElement.GetProperty("missions")[1];
        Assert.Equal("ACT", act.GetProperty("type").GetString());
        Assert.Equal("standardRobotsCustom", act.GetProperty("actionType").GetString());
        Assert.Equal("NONE", act.GetProperty("blockingType").GetString());
        Assert.False(act.TryGetProperty("mapId", out _), "ACT must not include mapId");
        Assert.False(act.TryGetProperty("stationId", out _), "ACT must not include stationId");
        Assert.False(act.TryGetProperty("actionName", out _), "wire must never include actionName");

        var firstParam = act.GetProperty("actionParameters")[0];
        Assert.Equal("id", firstParam.GetProperty("key").GetString());
        // CRITICAL: value must serialize as JSON number, not a string —
        // the spec example shows `"value": 131`, not `"value": "131"`.
        Assert.Equal(JsonValueKind.Number, firstParam.GetProperty("value").ValueKind);
        Assert.Equal(131, firstParam.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task CancelEnvelopeAsync_PutsOperationAgainstVendorOrderKey()
    {
        // Routes by RIOT3's own orderKey (NOT the DTMS upperKey via
        // ?isUpper=true). Verified empirically: with isUpper=true RIOT3
        // returns code "0" but silently no-ops on IN_QUEUE orders, leaving
        // the order live. The orderKey path is the only reliable form.
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.CancelEnvelopeAsync("272");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Contains("/api/v4/orders/272/operation", handler.LastUri);
        Assert.DoesNotContain("isUpper", handler.LastUri);
    }

    // RIOT3 returns HTTP 200 even on logical failures — the success signal
    // is in the response body (code "0"). Verify the silent-failure
    // detection so these never silently mark a Trip as dispatched when the
    // vendor actually refused.

    [Fact]
    public async Task SendOrderAsync_RejectionInBody_ReturnsFailure()
    {
        var handler = new ScriptedHandler("""{"code":"E100006","message":"station not on road network"}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.SendOrderAsync(new Riot3OrderRequest
        {
            UpperKey = "abc-G1", OrderName = "x", OrderType = "WORK", Priority = 10,
            StructureType = "sequence",
            Missions = new List<Riot3Mission> { new() { MissionKey = "m1", Type = "MOVE", Category = "agv", MapId = 1, StationId = 99 } }
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("E100006", result.Error);
        Assert.Contains("station not on road network", result.Error);
    }

    [Fact]
    public async Task CancelEnvelopeAsync_E110014_ReturnsNoVendorRecord()
    {
        // Gap #6: E110014 ("order is empty") is the vendor's soft-404
        // for cancel/pause/resume. The service surfaces it as the
        // NoVendorRecord outcome so handlers can apply per-command
        // policy (Cancel forgives, Pause/Resume escalate).
        var handler = new ScriptedHandler("""{"code":"E110014","message":"order is empty"}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.CancelEnvelopeAsync("nonexistent-G1");

        Assert.True(result.IsSuccess);
        Assert.Equal(DTMS.Transport.Amr.Models.Riot3OperationOutcome.NoVendorRecord, result.Value);
    }

    [Fact]
    public async Task CancelEnvelopeAsync_Http404_ReturnsNoVendorRecord()
    {
        // Hard 404 from RIOT3 (order purged / never received) maps to
        // the same outcome as the body-level E110014.
        var handler = new ScriptedHandler("""{"code":"E1","message":"not found"}""", System.Net.HttpStatusCode.NotFound);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.CancelEnvelopeAsync("missing-G1");

        Assert.True(result.IsSuccess);
        Assert.Equal(DTMS.Transport.Amr.Models.Riot3OperationOutcome.NoVendorRecord, result.Value);
    }

    [Fact]
    public async Task CancelEnvelopeAsync_OtherRejection_ReturnsFailure()
    {
        // Non-zero, non-empty codes (e.g. permission denied) stay as
        // Failure — handler should escalate to the operator.
        var handler = new ScriptedHandler("""{"code":"E100007","message":"permission denied"}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.CancelEnvelopeAsync("forbidden-G1");

        Assert.False(result.IsSuccess);
        Assert.Contains("E100007", result.Error);
    }

    [Fact]
    public async Task QueryService_RejectionInBody_ReturnsNull()
    {
        // RIOT3 returns 200 + "E110014" (order is empty) when an upperKey
        // does not exist on the vendor side. The reconciler must treat this
        // like a 404 — no record — not like a successful query.
        var handler = new ScriptedHandler("""{"code":"E110014","message":"order is empty"}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3OrderQueryService(client, NullLogger<Riot3OrderQueryService>.Instance);

        var data = await service.GetOrderByUpperKeyAsync("nonexistent-G1");

        Assert.Null(data);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? JsonBody { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string LastUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri?.ToString() ?? string.Empty;
            JsonBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":"0","message":"SUCCESS","data":{"orderKey":"ORD-1"}}""")
            };
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;
        public ScriptedHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) });
    }
}
