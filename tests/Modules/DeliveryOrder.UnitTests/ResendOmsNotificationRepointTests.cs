using DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Iam.Application.Callbacks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase 4 — the started resend was repointed off the deleted legacy
// IOmsShipmentClient onto the federated ISourceCallbackDispatcher (sync). This
// pins that: a successful resend dispatches to the "oms" partition and writes
// the UpstreamOmsManuallyResent audit (the UI's "resent" signal).
public class ResendOmsNotificationRepointTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder OmsOrder(Guid tripId, out Guid orderId)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-R-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS");
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        orderId = order.Id;
        return order;
    }

    [Fact]
    public async Task Resend_Success_DispatchesToOms_AndWritesManuallyResentAudit()
    {
        var tripId = Guid.NewGuid();
        var order = OmsOrder(tripId, out var orderId);

        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");

        var orders = Substitute.For<IDeliveryOrderRepository>();
        orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);
        var trips = Substitute.For<ITripRepository>();
        trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
        trips.GetRootTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(tripId);

        var formatter = Substitute.For<ICallbackPayloadFormatter>();
        formatter.FormatAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new CallbackPayload("application/json",
                System.Text.Encoding.UTF8.GetBytes("{\"shipmentId\":\"x\"}"),
                RelativePath: "/api/shipments"));
        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();

        var handler = new ResendOmsNotificationCommandHandler(
            formatter, dispatcher, trips, orders, audit, activity,
            NullLogger<ResendOmsNotificationCommandHandler>.Instance);

        var result = await handler.Handle(
            new ResendOmsNotificationCommand(orderId, tripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await dispatcher.Received(1).DispatchAsync("oms", Arg.Any<DTMS.SharedKernel.Outbox.OutboxMessage>(), Arg.Any<CancellationToken>());
        await audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e => e.EventType == "UpstreamOmsManuallyResent"),
            Arg.Any<CancellationToken>());
    }
}
