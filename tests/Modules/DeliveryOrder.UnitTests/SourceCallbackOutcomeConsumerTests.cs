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

// Phase S.5 — the outcome consumer produces the upstream-notification audit
// rows the order-detail UI reads. Phase C made it system-NEUTRAL: labels come
// from UpstreamCallbackAudit and the system lands in SystemKey — a SAP outcome
// writes the same vocabulary tagged 'sap' instead of masquerading as OMS.
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
        string eventType, bool success, int? statusCode, string systemKey = "oms")
    {
        var evt = new SourceCallbackOutcome(
            Guid.NewGuid(), DateTime.UtcNow, systemKey, eventType,
            OrderId: Guid.NewGuid(), TripId: Guid.NewGuid(),
            Success: success, StatusCode: statusCode, Detail: null);
        var ctx = Substitute.For<ConsumeContext<SourceCallbackOutcome>>();
        ctx.Message.Returns(evt);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Theory]
    [InlineData("shipment.started.v1", true, null, UpstreamCallbackAudit.Notified)]
    [InlineData("shipment.started.v1", false, 404, UpstreamCallbackAudit.Rejected)]
    [InlineData("shipment.started.v1", false, 503, UpstreamCallbackAudit.NotifyFailed)]
    [InlineData("shipment.started.v1", false, null, UpstreamCallbackAudit.NotifyFailed)]
    [InlineData("shipment.arrived.v1", true, null, UpstreamCallbackAudit.ArrivedNotified)]
    [InlineData("shipment.arrived.v1", false, 422, UpstreamCallbackAudit.ArrivedRejected)]
    [InlineData("shipment.arrived.v1", false, 500, UpstreamCallbackAudit.ArrivedNotifyFailed)]
    [InlineData("shipment.cancelled.v1", true, null, UpstreamCallbackAudit.CancelledNotified)]
    [InlineData("shipment.cancelled.v1", false, 409, UpstreamCallbackAudit.CancelledRejected)]
    [InlineData("shipment.cancelled.v1", false, 502, UpstreamCallbackAudit.CancelledNotifyFailed)]
    public async Task Consume_MapsOutcomeToGenericAuditType(
        string eventType, bool success, int? statusCode, string expected)
    {
        var (consumer, audit, activity) = Build();

        await consumer.Consume(Ctx(eventType, success, statusCode));

        await audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e => e.EventType == expected && e.SystemKey == "oms"),
            Arg.Any<CancellationToken>());
        // 5th positional arg is eventType; the trailing named arg is systemKey.
        await activity.Received(1).AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            expected, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<Guid?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>(),
            Arg.Any<string>(), Arg.Any<string>(), systemKey: "oms");
    }

    // Phase C's reason for existing: a SAP outcome must carry SystemKey='sap'
    // with the SAME neutral label — never an OMS-branded one.
    [Fact]
    public async Task Consume_SapOutcome_WritesNeutralLabel_TaggedSap()
    {
        var (consumer, audit, _) = Build();

        await consumer.Consume(Ctx("shipment.started.v1", true, null, systemKey: "sap"));

        await audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e =>
                e.EventType == UpstreamCallbackAudit.Notified && e.SystemKey == "sap"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_IgnoresNonShipmentEventTypes()
    {
        var (consumer, audit, _) = Build();

        await consumer.Consume(Ctx("order.delivered.v1", true, null));

        await audit.DidNotReceive().AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }
}
