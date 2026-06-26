using DTMS.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Infrastructure.Sagas;
using DTMS.Planning.IntegrationEvents;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Planning.UnitTests;

// T2 Phase 2 step 1 (A4) — three smoke tests covering exactly the two new
// behaviours from A1 + A2 and one existing behaviour. The point isn't to
// re-test MassTransit's state-machine engine, it's to lock in the saga's
// observable contract so a future refactor breaks tests, not production:
//
//   - Initially(OrderConfirmed) lands the saga at AwaitingPlan
//   - Redelivered OrderConfirmed on AwaitingPlan is silently ignored (no
//     NotAcceptedStateMachineException, no state change, no exception)
//   - PlanRequested on AwaitingPlan moves the saga to Planning
//
// Uses MassTransit's in-memory test harness so the assertions exercise the
// real state machine instead of inspecting properties in isolation.
public class T2_DeliveryOrderSagaStateMachineTests
{
    private static ServiceProvider BuildHarness()
    {
        var services = new ServiceCollection();
        services.AddSingleton(NullLogger<DeliveryOrderSagaStateMachine>.Instance);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddSagaStateMachine<DeliveryOrderSagaStateMachine, DeliveryOrderSagaInstance>()
             .InMemoryRepository();
        });
        return services.BuildServiceProvider(true);
    }

    private static async Task<(ITestHarness harness, ISagaStateMachineTestHarness<DeliveryOrderSagaStateMachine, DeliveryOrderSagaInstance> saga)>
        StartedHarness()
    {
        var provider = BuildHarness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = harness.GetSagaStateMachineHarness<DeliveryOrderSagaStateMachine, DeliveryOrderSagaInstance>();
        return (harness, saga);
    }

    [Fact]
    public async Task Initially_OrderConfirmed_EntersAwaitingPlan()
    {
        var (harness, saga) = await StartedHarness();
        var orderId = Guid.NewGuid();

        await harness.Bus.Publish(NewConfirmedEvent(orderId));

        (await saga.Created.Any(s => s.CorrelationId == orderId)).Should().BeTrue(
            "the first OrderConfirmed must create a saga instance");
        var instance = saga.Created.ContainsInState(orderId, saga.StateMachine, saga.StateMachine.AwaitingPlan);
        instance.Should().NotBeNull("the saga should land at AwaitingPlan on its first event");
    }

    [Fact]
    public async Task DuringAwaitingPlan_OrderConfirmedRedelivery_StaysInAwaitingPlanWithoutException()
    {
        // Step 1's redelivery-dedup behaviour. The watchdog, retry policy, or
        // admin /replan can republish DeliveryOrderConfirmed for the same
        // order — MassTransit would default to NotAcceptedStateMachineException
        // without the explicit Ignore handlers we added in A1.
        var (harness, saga) = await StartedHarness();
        var orderId = Guid.NewGuid();

        await harness.Bus.Publish(NewConfirmedEvent(orderId));
        await harness.Bus.Publish(NewConfirmedEvent(orderId));  // redelivery
        await harness.Bus.Publish(NewConfirmedEvent(orderId));  // and again

        // No faults on the saga endpoint = the Ignore handlers did their job.
        (await harness.Sent.Any<Fault<DeliveryOrderConfirmedIntegrationEventV1>>()).Should().BeFalse(
            "redelivered OrderConfirmed must be ignored, not faulted");
        var instance = saga.Created.ContainsInState(orderId, saga.StateMachine, saga.StateMachine.AwaitingPlan);
        instance.Should().NotBeNull("the saga should remain at AwaitingPlan after redeliveries");
    }

    [Fact]
    public async Task DuringAwaitingPlan_PlanRequested_TransitionsToPlanning()
    {
        // The first real transition beyond the POC's Initially block.
        // Publishing OrderPlanRequestedIntegrationEventV1 with the same
        // correlation id should move the saga AwaitingPlan -> Planning.
        //
        // The two Bus.Publish calls + ContainsInState chain can race in the
        // in-memory harness — the second event can arrive before the saga
        // has been materialised by the first. Wait on Consumed.Any<T> after
        // each publish so the assertion sees the post-transition state.
        var (harness, saga) = await StartedHarness();
        var orderId = Guid.NewGuid();

        await harness.Bus.Publish(NewConfirmedEvent(orderId));
        (await harness.Consumed.Any<DeliveryOrderConfirmedIntegrationEventV1>(
            m => m.Context.Message.DeliveryOrderId == orderId)).Should().BeTrue();

        await harness.Bus.Publish(NewPlanRequestedEvent(orderId));
        (await harness.Consumed.Any<OrderPlanRequestedIntegrationEventV1>(
            m => m.Context.Message.DeliveryOrderId == orderId)).Should().BeTrue();

        var instance = saga.Created.ContainsInState(orderId, saga.StateMachine, saga.StateMachine.Planning);
        instance.Should().NotBeNull("PlanRequested on AwaitingPlan should transition to Planning");
    }

    private static DeliveryOrderConfirmedIntegrationEventV1 NewConfirmedEvent(Guid orderId) =>
        new(
            EventId: Guid.NewGuid(),
            OccurredOn: DateTime.UtcNow,
            DeliveryOrderId: orderId,
            Priority: "Normal",
            EarliestUtc: null,
            LatestUtc: DateTime.UtcNow.AddHours(4),
            SubmittedAt: DateTime.UtcNow,
            Items: Array.Empty<ItemSummaryDto>(),
            RequestedTransportMode: "AMR");

    private static OrderPlanRequestedIntegrationEventV1 NewPlanRequestedEvent(Guid orderId) =>
        new(
            EventId: Guid.NewGuid(),
            OccurredOn: DateTime.UtcNow,
            DeliveryOrderId: orderId);
}
