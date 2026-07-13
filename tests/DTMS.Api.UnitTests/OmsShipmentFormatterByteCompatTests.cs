using System.Text;
using System.Text.Json;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using DTMS.OmsAdapter.Abstractions.Models;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// Phase S.5 (B2) — the new OMS shipment formatters must emit bodies BYTE-
// IDENTICAL to what the legacy adapter POSTed (serialization of
// OmsShipmentNotification / OmsArrivedNotification), so OMS sees no change.
public class OmsShipmentFormatterByteCompatTests
{
    private static string LegacyBytes<T>(T model) =>
        Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(model));

    private static async Task<CallbackPayload> Format(ICallbackPayloadFormatter f, object ctx) =>
        await f.FormatAsync(ctx, CancellationToken.None);

    [Fact]
    public async Task Started_BodyMatchesLegacy_AndRoutesToShipmentsPath()
    {
        var legacy = LegacyBytes(new OmsShipmentNotification(
            "root-trip-1", "FAN1_STANDARD_NO3",
            new[] { new OmsLot("LOT-A"), new OmsLot("LOT-B") }));

        var payload = await Format(new OmsShipmentStartedFormatter(),
            new OmsShipmentStartedContext("root-trip-1", "FAN1_STANDARD_NO3",
                new[] { "LOT-A", "LOT-B" }));

        Encoding.UTF8.GetString(payload.Body).Should().Be(legacy);
        payload.RelativePath.Should().Be("/api/shipments");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    [Fact]
    public async Task Started_NullDeliveryBy_SerializesIdentically()
    {
        var legacy = LegacyBytes(new OmsShipmentNotification(
            "root-trip-1", null, new[] { new OmsLot("LOT-A") }));

        var payload = await Format(new OmsShipmentStartedFormatter(),
            new OmsShipmentStartedContext("root-trip-1", null, new[] { "LOT-A" }));

        Encoding.UTF8.GetString(payload.Body).Should().Be(legacy);
        Encoding.UTF8.GetString(payload.Body).Should().Contain("\"deliveryBy\":null");
    }

    [Fact]
    public async Task Arrived_BodyMatchesLegacy_AndShipmentIdInPath()
    {
        var legacy = LegacyBytes(new OmsArrivedNotification(
            new[] { new OmsLot("LOT-A"), new OmsLot("LOT-B") }));

        var payload = await Format(new OmsShipmentArrivedFormatter(),
            new OmsShipmentArrivedContext("root-trip-9", new[] { "LOT-A", "LOT-B" }));

        Encoding.UTF8.GetString(payload.Body).Should().Be(legacy);
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
