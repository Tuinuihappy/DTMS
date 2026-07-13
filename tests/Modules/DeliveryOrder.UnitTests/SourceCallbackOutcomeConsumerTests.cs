using DTMS.DeliveryOrder.Application.Consumers;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

// Phase S.5 — the outcome consumer restores the OMS-notification audit rows the
// order-detail UI reads, mapping a federated callback's terminal outcome onto
// the exact legacy audit event types.
public class SourceCallbackOutcomeConsumerTests
{
    private static (SourceCallbackOutcomeConsumer consumer,
        IOrderAuditEventRepository audit, IOrderActivityProjectionStore activity) Build()
    {
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        return (new SourceCallbackOutcomeConsumer(
            audit, activity, NullLogger<SourceCallbackOutcomeConsumer>.Instance), audit, activity);
    }

    private static ConsumeContext<SourceCallbackOutcome> Ctx(
        string eventType, bool success, int? statusCode)
    {
        var evt = new SourceCallbackOutcome(
            Guid.NewGuid(), DateTime.UtcNow, "oms", eventType,
            OrderId: Guid.NewGuid(), TripId: Guid.NewGuid(),
            Success: success, StatusCode: statusCode, Detail: null);
        var ctx = Substitute.For<ConsumeContext<SourceCallbackOutcome>>();
        ctx.Message.Returns(evt);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Theory]
    [InlineData("shipment.started.v1", true, null, "UpstreamOmsNotified")]
    [InlineData("shipment.started.v1", false, 404, "UpstreamOmsRejected")]
    [InlineData("shipment.started.v1", false, 503, "UpstreamOmsNotifyFailed")]
    [InlineData("shipment.started.v1", false, null, "UpstreamOmsNotifyFailed")]
    [InlineData("shipment.arrived.v1", true, null, "UpstreamOmsArrivedNotified")]
    [InlineData("shipment.arrived.v1", false, 422, "UpstreamOmsArrivedRejected")]
    [InlineData("shipment.arrived.v1", false, 500, "UpstreamOmsArrivedNotifyFailed")]
    public async Task Consume_MapsOutcomeToAuditType(
        string eventType, bool success, int? statusCode, string expected)
    {
        var (consumer, audit, activity) = Build();

        await consumer.Consume(Ctx(eventType, success, statusCode));

        await audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e => e.EventType == expected), Arg.Any<CancellationToken>());
        // 5th positional arg is eventType. Signature: (projectorName, eventId,
        // orderId, category, eventType, details, actorId[string?], occurredAt,
        // relatedTripId, attemptNumber, ct[, channel, displayName]).
        await activity.Received(1).AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            expected, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<Guid?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_IgnoresNonShipmentEventTypes()
    {
        var (consumer, audit, _) = Build();

        await consumer.Consume(Ctx("order.delivered.v1", true, null));

        await audit.DidNotReceive().AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }
}
