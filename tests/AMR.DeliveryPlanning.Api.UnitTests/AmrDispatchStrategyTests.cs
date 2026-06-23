using AMR.DeliveryPlanning.Api.Adapters;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AMR.DeliveryPlanning.Api.UnitTests;

// Phase 1.2 — AmrDispatchStrategy is scaffolding: registered so the
// registry can resolve it, but throws if invoked because production AMR
// dispatch still runs through DispatchOrderTemplateService. Tests pin
// these contract guarantees so Phase 3 has a clear "wire the body
// without breaking the contract" target.
public class AmrDispatchStrategyTests
{
    private readonly IDispatchOrderTemplateService _planning = Substitute.For<IDispatchOrderTemplateService>();

    private AmrDispatchStrategy CreateStrategy() =>
        new(_planning, NullLogger<AmrDispatchStrategy>.Instance);

    [Fact]
    public void Mode_IsAmr()
    {
        // The mode property is what the registry indexes by — if this
        // ever changed, the registry would silently route Amr trips to
        // a different strategy. Lock it in.
        var strategy = CreateStrategy();

        strategy.Mode.Should().Be(TransportMode.Amr);
    }

    [Fact]
    public void Implements_IDispatchStrategy()
    {
        // Sanity check — interface change shouldn't accidentally drop
        // the contract that the registry depends on.
        var strategy = CreateStrategy();

        strategy.Should().BeAssignableTo<IDispatchStrategy>();
    }

    [Fact]
    public async Task DispatchAsync_ReturnsFailure_WithPhase3RefactorMessage()
    {
        // The strategy must NOT run the dispatch in Phase 1.2 — that
        // would create a duplicate against the production
        // DispatchOrderTemplateService path. Failing fast (as a typed
        // Result, not an exception) lets callers handle it gracefully
        // if the registry accidentally routes here.
        var strategy = CreateStrategy();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1");

        var result = await strategy.DispatchAsync(trip, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Phase 1.2 scaffolding");
        result.Error.Should().Contain("DispatchOrderTemplateService");
    }

    [Fact]
    public async Task DispatchAsync_DoesNotInvokePlanning_InScaffoldingMode()
    {
        // Critical: the Phase 1.2 stub must not accidentally call into
        // Planning's dispatch service — that's exactly the "double
        // dispatch" risk the scaffolding exists to prevent. If Phase 3
        // refactor partially wires this strategy in before the consumer
        // refactor lands, this test holds the line.
        var strategy = CreateStrategy();
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1");

        await strategy.DispatchAsync(trip, CancellationToken.None);

        await _planning.DidNotReceiveWithAnyArgs().DispatchByRouteAsync(
            default, default, default, default!, default, default, default,
            default, default, default, default, default, default);
    }
}
