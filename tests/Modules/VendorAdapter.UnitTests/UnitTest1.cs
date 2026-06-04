using System.Net;
using System.Text.Json;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;
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
    public async Task CancelEnvelopeAsync_PutsOperationAgainstUpperKey()
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.CancelEnvelopeAsync("abc123-G1");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Contains("/api/v4/orders/abc123-G1/operation", handler.LastUri);
        Assert.Contains("isUpper=true", handler.LastUri);
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
}
