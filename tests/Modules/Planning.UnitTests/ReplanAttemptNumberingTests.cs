using DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Planning.Application.Commands.CreateJobAnchor;
using DTMS.Planning.Application.Consumers;
using DTMS.SharedKernel;
using DTMS.SharedKernel.Diagnostics;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Planning.UnitTests;

/// <summary>
/// A re-plan (revise-and-resend, admin redispatch, Release, redelivery) must
/// neither reuse a dead group's upperKey — CreateEnvelopeTrip is idempotent on
/// it and would hand back the dead trip — nor send a second robot for a group
/// whose trip is still running or already done.
/// </summary>
public class ReplanAttemptNumberingTests
{
    private static readonly Guid OrderId = Guid.NewGuid();

    private static TripAttemptRef TripFor(int group, int attempt, TripStatus status, Guid? previous = null)
        => new(Guid.NewGuid(), EnvelopeUpperKey.Build(OrderId, group, attempt), attempt, status, previous);

    [Fact]
    public void FirstPlan_NoPriorTrips_IsAttemptOne()
    {
        var plan = DeliveryOrderValidatedConsumer.NextAttempt([], OrderId, 1);

        plan.Skip.Should().BeFalse();
        plan.AttemptNumber.Should().Be(1);
        plan.PreviousAttemptId.Should().BeNull();
    }

    [Fact]
    public void Replan_AfterEndedTip_GoesOnePastTheTipAndLinksToIt()
    {
        var first = TripFor(1, 1, TripStatus.Failed);
        var retry = TripFor(1, 2, TripStatus.Rejected, first.Id);

        var plan = DeliveryOrderValidatedConsumer.NextAttempt([first, retry], OrderId, 1);

        plan.Skip.Should().BeFalse();
        plan.AttemptNumber.Should().Be(3);
        plan.PreviousAttemptId.Should().Be(retry.Id);
        EnvelopeUpperKey.Build(OrderId, 1, plan.AttemptNumber)
            .Should().NotBe(first.UpperKey).And.NotBe(retry.UpperKey);
    }

    [Fact]
    public void Replan_OnlyCountsTripsOfTheSameGroup()
    {
        var plan = DeliveryOrderValidatedConsumer.NextAttempt(
            [TripFor(2, 4, TripStatus.Failed)], OrderId, 1);

        plan.Skip.Should().BeFalse();
        plan.AttemptNumber.Should().Be(1);
        plan.PreviousAttemptId.Should().BeNull();
    }

    [Theory]
    [InlineData(TripStatus.Created)]
    [InlineData(TripStatus.InProgress)]
    [InlineData(TripStatus.Hang)]
    [InlineData(TripStatus.Held)]
    [InlineData(TripStatus.Completed)]
    public void RunningOrCompletedTip_IsSkipped(TripStatus status)
    {
        var tip = TripFor(1, 1, status);

        var plan = DeliveryOrderValidatedConsumer.NextAttempt([tip], OrderId, 1);

        plan.Skip.Should().BeTrue();
        plan.ExistingTrip.Should().Be(tip);
    }

    [Fact]
    public async Task Release_OfAPartlyDoneOrder_DispatchesOnlyTheFailedGroup()
    {
        // Group 1 delivered, group 2 failed → only group 2 goes to the vendor,
        // as attempt 2 linked to its failed trip.
        var g1 = TripFor(1, 1, TripStatus.Completed);
        var g2 = TripFor(2, 1, TripStatus.Failed);
        var trips = Substitute.For<ITripRepository>();
        trips.GetAttemptRefsByDeliveryOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(new List<TripAttemptRef> { g1, g2 });

        var strategy = Substitute.For<IDispatchStrategy>();
        strategy.Mode.Returns(DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr);
        strategy.GroupItems(Arg.Any<IReadOnlyList<DispatchGroupItem>>()).Returns(new List<DispatchGroup>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), [new DispatchGroupItem("SKU-1", null, null)]),
            new(Guid.NewGuid(), Guid.NewGuid(), [new DispatchGroupItem("SKU-2", null, null)]),
        });
        strategy.DispatchGroupAsync(Arg.Any<DispatchGroupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(Guid.NewGuid(), "V", "T")));

        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr).Returns(true);
        registry.Get(DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr).Returns(strategy);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<MarkOrderPlanningCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        sender.Send(Arg.Any<CreateJobAnchorCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure("no anchor in this test"));

        var consumer = new DeliveryOrderValidatedConsumer(
            registry, Substitute.For<ISelfManagedDispatchService>(), sender,
            new WorkflowMetrics(), trips, NullLogger<DeliveryOrderValidatedConsumer>.Instance);

        var ctx = Substitute.For<ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1>>();
        ctx.Message.Returns(new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, OrderId, "Normal",
            EarliestUtc: null, LatestUtc: null, SubmittedAt: DateTime.UtcNow,
            Items:
            [
                new ItemSummaryDto("SKU-1", 1.0, Guid.NewGuid(), Guid.NewGuid()),
                new ItemSummaryDto("SKU-2", 1.0, Guid.NewGuid(), Guid.NewGuid()),
            ],
            RequestedTransportMode: "Amr"));
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        await strategy.Received(1).DispatchGroupAsync(Arg.Any<DispatchGroupRequest>(), Arg.Any<CancellationToken>());
        await strategy.Received(1).DispatchGroupAsync(
            Arg.Is<DispatchGroupRequest>(r =>
                r.GroupIndex == 2
                && r.AttemptNumber == 2
                && r.PreviousAttemptId == g2.Id
                && r.UpperKey == EnvelopeUpperKey.Build(OrderId, 2, 2)),
            Arg.Any<CancellationToken>());
    }
}
