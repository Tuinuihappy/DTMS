using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Exceptions;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OmsAdapter.IntegrationTests;

// Pins the permanent-vs-transient classification of OmsHttpShipmentClient.
//
// The retry strategy in ModuleServiceRegistration.cs hangs off these two
// exception types — MassTransit Ignore<OmsPermanentException>() fast-fails
// data-rejection errors to DLQ, while OmsTransientException flows through
// the normal retry ladder. Misclassifying a status code here silently
// breaks that contract: a 503 mapped to permanent would dead-letter
// during a transient OMS blip; a 404 mapped to transient would retry an
// unfixable "LotNo not found" for ~21 minutes and drag fault rate up
// until the Kill Switch trips the entire endpoint.
public class StatusCodeClassificationTests : IClassFixture<HttpOmsShipmentClientFixture>
{
    private readonly HttpOmsShipmentClientFixture _fx;

    public StatusCodeClassificationTests(HttpOmsShipmentClientFixture fx) => _fx = fx;

    private static OmsShipmentNotification SampleNotification() => new(
        ShipmentId: Guid.NewGuid().ToString(),
        DeliveryBy: "AMR",
        Lots: new[] { new OmsLot("LOT-001") });

    // 4xx (except 408/425/429) = client error that won't fix itself on retry.
    // Bad request, missing auth, missing resource, unprocessable entity —
    // operator must inspect and fix the data before resending.
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task NotifyShipmentStartedAsync_4xx_Throws_OmsPermanentException(int statusCode)
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody("bad data"));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(SampleNotification(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OmsPermanentException>();
        ex.Which.StatusCode.Should().Be((System.Net.HttpStatusCode)statusCode);
        ex.Which.ResponseBody.Should().Be("bad data");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task NotifyShipmentArrivedAsync_4xx_Throws_OmsPermanentException(int statusCode)
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/arrived")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody("LotNo not found: rr."));

        var act = () => _fx.Client.NotifyShipmentArrivedAsync(
            shipmentId, new[] { new OmsLot("rr") }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OmsPermanentException>();
        ex.Which.StatusCode.Should().Be((System.Net.HttpStatusCode)statusCode);
        ex.Which.ResponseBody.Should().Be("LotNo not found: rr.");
    }

    // 408 / 425 / 429 are 4xx by status but RFC-defined as retry-able:
    // request timeout, too-early, rate limit. Mapping them to permanent
    // would dead-letter the message on a transient hiccup.
    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    public async Task NotifyShipmentStartedAsync_RetryableClientErrors_Throws_OmsTransientException(int statusCode)
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(SampleNotification(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OmsTransientException>();
        ex.Which.StatusCode.Should().Be((System.Net.HttpStatusCode)statusCode);
    }

    // All 5xx → transient. OMS-side problem, retry should eventually
    // recover once OMS comes back.
    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task NotifyShipmentStartedAsync_5xx_Throws_OmsTransientException(int statusCode)
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(SampleNotification(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OmsTransientException>();
        ex.Which.StatusCode.Should().Be((System.Net.HttpStatusCode)statusCode);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task NotifyShipmentArrivedAsync_5xx_Throws_OmsTransientException(int statusCode)
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/arrived")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode));

        var act = () => _fx.Client.NotifyShipmentArrivedAsync(
            shipmentId, new[] { new OmsLot("LOT-A") }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OmsTransientException>();
        ex.Which.StatusCode.Should().Be((System.Net.HttpStatusCode)statusCode);
    }

    // Both classified exceptions inherit HttpRequestException so existing
    // catch sites (Polly policies, fault consumers, resend handlers) stay
    // correct — only the type discrimination is new.
    [Fact]
    public async Task OmsPermanentException_Is_HttpRequestException()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(404).WithBody("nope"));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(SampleNotification(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task OmsTransientException_Is_HttpRequestException()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(SampleNotification(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // Regression guard: 409 on /shipments is still the special no-op
    // success path (OMS dedup says "already registered"). Must NOT be
    // reclassified as permanent — that would break idempotent retries.
    [Fact]
    public async Task NotifyShipmentStartedAsync_409_Still_NoOp_Success()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409).WithBody("already registered"));

        await _fx.Client.NotifyShipmentStartedAsync(SampleNotification(), CancellationToken.None);
        _fx.Server.LogEntries.Should().ContainSingle();
    }
}
