using System.Net;
using System.Text.Json;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace VendorAdapter.UnitTests;

public class UnitTest1
{
    [Fact]
    public async Task SendTaskAsync_UsesVendorVehicleKeyAsAppointVehicleKey()
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);
        var appVehicleId = Guid.NewGuid();
        var deviceKey = "SEER-001";

        var result = await service.SendTaskAsync(appVehicleId, new RobotTaskCommand
        {
            TaskId = Guid.NewGuid(),
            VendorVehicleKey = deviceKey,
            Action = RobotActionType.MOVE,
            TargetNodeId = "ST-001"
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.JsonBody);

        using var doc = JsonDocument.Parse(handler.JsonBody!);
        var appointVehicleKey = doc.RootElement.GetProperty("appointVehicleKey").GetString();

        Assert.Equal(deviceKey, appointVehicleKey);
        Assert.NotEqual(appVehicleId.ToString(), appointVehicleKey);
    }

    [Fact]
    public async Task SendTaskAsync_WithoutVendorVehicleKey_FailsWithoutHttpCall()
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://riot3.local") };
        var service = new Riot3CommandService(client, NullLogger<Riot3CommandService>.Instance);

        var result = await service.SendTaskAsync(Guid.NewGuid(), new RobotTaskCommand
        {
            TaskId = Guid.NewGuid(),
            Action = RobotActionType.MOVE,
            TargetNodeId = "ST-001"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? JsonBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            JsonBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":"0","message":"SUCCESS"}""")
            };
        }
    }
}
