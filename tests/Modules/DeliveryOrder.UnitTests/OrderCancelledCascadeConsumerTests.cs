using DTMS.DeliveryOrder.Application.Consumers;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Application.Commands.CancelTrip;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

// Cancelling an order calls off its trips, and each of those cancels has to say
// who asked. The event carries one field for both kinds of caller —
// TriggeredBy is "86347852" for an operator and "oms" for a source system — so
// Channel is the discriminator that decides which of ActionBy / Origin it fills.
public class OrderCancelledCascadeConsumerTests
{
    [Fact]
    public async Task SystemApiCancel_TagsTripsWithTheCallingSystem()
    {
        var (consumer, sender, actor) = Build(ActiveTrip(out var orderId, out var tripId));

        await consumer.Consume(Ctx(Cancelled(orderId, channel: "SystemApi",
            triggeredBy: "oms", sourceSystem: "oms")));

        var cmd = await Captured(sender);
        cmd.TripId.Should().Be(tripId);
        cmd.Origin.Should().Be("oms");
        cmd.ActionBy.Should().BeNull("a system is not a person");
        actor.Received(1).BeginScope(Arg.Is<ActorContext>(a =>
            a.PrincipalId == "system:oms" && a.Channel == SourceChannel.SystemApi));
    }

    [Fact]
    public async Task OperatorCancelOfASourceOwnedOrder_DoesNotTagTripsWithTheOwner()
    {
        // The whole reason this branch reads TriggeredBy instead of SourceSystem.
        // SourceSystem is who OWNS the order — here "oms" — so using it would
        // stamp via=oms onto a cancel a DTMS operator asked for. Goes red the
        // moment anyone simplifies the branch back to evt.SourceSystem.
        var (consumer, sender, actor) = Build(ActiveTrip(out var orderId, out _));

        await consumer.Consume(Ctx(Cancelled(orderId, channel: "ManualWeb",
            triggeredBy: "86347852", sourceSystem: "oms")));

        var cmd = await Captured(sender);
        cmd.Origin.Should().BeNull("an operator is not a source system");
        cmd.ActionBy.Should().Be("86347852");
        // The caller's own channel carries over, so the cascaded trip row in the
        // timeline shows the same Web badge as the order row that caused it.
        actor.Received(1).BeginScope(Arg.Is<ActorContext>(a =>
            a.PrincipalId == "user:86347852" && a.Channel == SourceChannel.ManualWeb));
    }

    [Fact]
    public async Task InternalCascade_CarriesNeitherActorNorOrigin()
    {
        // TripCancelledConsumer's own order-cancel arm runs with no principal,
        // so TriggeredBy falls back to the literal "system". Recording that as
        // the person who cancelled is worse than recording nobody.
        var (consumer, sender, _) = Build(ActiveTrip(out var orderId, out _));

        await consumer.Consume(Ctx(Cancelled(orderId, channel: "InternalJob",
            triggeredBy: ActorContext.System.Source, sourceSystem: "oms")));

        var cmd = await Captured(sender);
        cmd.ActionBy.Should().BeNull();
        cmd.Origin.Should().BeNull();
    }

    [Fact]
    public async Task NoActiveTrips_SendsNothing()
    {
        // The pre-dispatch window, and the reason the order/trip cascade pair
        // cannot loop.
        var orderId = Guid.NewGuid();
        var done = Trip.CreateForEnvelope(orderId, "UK-DONE", "VK-1");
        done.MarkVendorStarted();
        done.MarkVendorCompleted();
        var (consumer, sender, _) = Build(new List<Trip> { done }, orderId);

        await consumer.Consume(Ctx(Cancelled(orderId, "SystemApi", "oms", "oms")));

        await sender.DidNotReceive().Send(
            Arg.Any<CancelTripCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneTripFailing_DoesNotAbortTheRest()
    {
        // Best-effort is documented on the class but has never been exercised.
        var orderId = Guid.NewGuid();
        var trips = new List<Trip> { StartedTrip(orderId, "UK-1"), StartedTrip(orderId, "UK-2") };
        var (consumer, sender, _) = Build(trips, orderId);
        sender.Send(Arg.Any<CancelTripCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("vendor said no"), Result.Success());

        await consumer.Consume(Ctx(Cancelled(orderId, "SystemApi", "oms", "oms")));

        await sender.Received(2).Send(
            Arg.Any<CancelTripCommand>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static async Task<CancelTripCommand> Captured(ISender sender)
    {
        var calls = sender.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ISender.Send))
            .Select(c => c.GetArguments()[0])
            .OfType<CancelTripCommand>()
            .ToList();
        calls.Should().ContainSingle();
        await Task.CompletedTask;
        return calls[0];
    }

    private static DeliveryOrderCancelledIntegrationEventV1 Cancelled(
        Guid orderId, string channel, string triggeredBy, string sourceSystem)
        => new(Guid.NewGuid(), DateTime.UtcNow, orderId, "operator stopped it",
            TriggeredBy: triggeredBy, CorrelationId: null,
            Channel: channel, DisplayName: null, SourceSystem: sourceSystem);

    private static List<Trip> ActiveTrip(out Guid orderId, out Guid tripId)
    {
        orderId = Guid.NewGuid();
        var trip = StartedTrip(orderId, "UK-1");
        tripId = trip.Id;
        return new List<Trip> { trip };
    }

    private static Trip StartedTrip(Guid orderId, string upperKey)
    {
        var trip = Trip.CreateForEnvelope(orderId, upperKey, "VK-1");
        trip.MarkVendorStarted();
        return trip;
    }

    private static (OrderCancelledCascadeConsumer consumer, ISender sender, ICurrentActorContext actor)
        Build(List<Trip> trips, Guid? orderId = null)
    {
        var tripRepo = Substitute.For<ITripRepository>();
        tripRepo.GetByDeliveryOrderIdAsync(orderId ?? trips[0].DeliveryOrderId, Arg.Any<CancellationToken>())
            .Returns(trips);

        var sender = Substitute.For<ISender>();
        // Result has no public parameterless ctor, so an unstubbed Send returns
        // a Task<Result> of null and every assertion downstream NREs.
        sender.Send(Arg.Any<CancelTripCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var actor = Substitute.For<ICurrentActorContext>();
        actor.BeginScope(Arg.Any<ActorContext>()).Returns(Substitute.For<IDisposable>());

        return (new OrderCancelledCascadeConsumer(
            tripRepo, sender, actor, NullLogger<OrderCancelledCascadeConsumer>.Instance),
            sender, actor);
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
