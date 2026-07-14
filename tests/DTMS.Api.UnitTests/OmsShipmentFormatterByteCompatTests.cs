using System.Text;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// Phase S.5 — golden wire-format for the OMS shipment formatters. These bytes
// were byte-identical to the legacy OmsShipmentNotification / OmsArrivedNotification
// serialization; after the legacy adapter was removed (Phase 4) the expected
// JSON is pinned inline so the OMS contract can't drift.
public class OmsShipmentFormatterByteCompatTests
{
    private static async Task<string> Body(ICallbackPayloadFormatter f, object ctx) =>
        Encoding.UTF8.GetString((await f.FormatAsync(ctx, CancellationToken.None)).Body);

    [Fact]
    public async Task Started_BodyMatchesContract_AndRoutesToShipmentsPath()
    {
        var payload = await new OmsShipmentStartedFormatter().FormatAsync(
            new OmsShipmentStartedContext("root-trip-1", "FAN1_STANDARD_NO3",
                new[] { "LOT-A", "LOT-B" }), CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"shipmentId\":\"root-trip-1\",\"deliveryBy\":\"FAN1_STANDARD_NO3\"," +
            "\"lots\":[{\"lotNo\":\"LOT-A\"},{\"lotNo\":\"LOT-B\"}]}");
        payload.RelativePath.Should().Be("/api/shipments");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    [Fact]
    public async Task Started_NullDeliveryBy_SerializesNull()
    {
        var body = await Body(new OmsShipmentStartedFormatter(),
            new OmsShipmentStartedContext("root-trip-1", null, new[] { "LOT-A" }));

        body.Should().Be(
            "{\"shipmentId\":\"root-trip-1\",\"deliveryBy\":null,\"lots\":[{\"lotNo\":\"LOT-A\"}]}");
    }

    [Fact]
    public async Task Arrived_BodyMatchesContract_AndShipmentIdInPath()
    {
        var payload = await new OmsShipmentArrivedFormatter().FormatAsync(
            new OmsShipmentArrivedContext("root-trip-9", new[] { "LOT-A", "LOT-B" }),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"lots\":[{\"lotNo\":\"LOT-A\"},{\"lotNo\":\"LOT-B\"}]}");
        payload.RelativePath.Should().Be("/api/shipments/root-trip-9/arrived");
    }

    [Fact]
    public async Task Formatter_RejectsWrongContextType()
    {
        var act = async () => await new OmsShipmentStartedFormatter()
            .FormatAsync("not-a-context", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
