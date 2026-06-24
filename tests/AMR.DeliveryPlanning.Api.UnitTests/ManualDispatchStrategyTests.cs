using AMR.DeliveryPlanning.Api.Adapters;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMR.DeliveryPlanning.Api.UnitTests;

// Phase 3c — ManualDispatchStrategy is a stub registered so the
// IDispatchStrategyRegistry can resolve TransportMode.Manual, but the
// real operator-assignment + push-notification flow is Phase 4. Tests
// pin the stub contract so:
//   - Mode == Manual (registry key)
//   - Implements IDispatchStrategy
//   - DispatchGroupAsync returns Failure (not Throw) — caller's failure
//     path lands the order at Failed instead of crashing the consumer
//   - The Failure message names Phase 4 explicitly so an operator
//     reading the audit knows exactly what's missing
//
// When Phase 4 replaces the stub body, these tests will fail loudly —
// the Phase 4 commit should rewrite them to assert the real flow
// (operator assigned, push notification dispatched, Trip created with
// no vendor key, etc.).
public class ManualDispatchStrategyTests
{
    private ManualDispatchStrategy CreateStrategy() =>
        new(NullLogger<ManualDispatchStrategy>.Instance);

    [Fact]
    public void Mode_IsManual()
    {
        // Registry indexes by this; a wrong value would silently route
        // Manual trips to AMR (or throw TransportModeNotEnabledException).
        var strategy = CreateStrategy();

        strategy.Mode.Should().Be(TransportMode.Manual);
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
    public async Task DispatchGroupAsync_ReturnsFailure_WithPhase4Message()
    {
        // The stub MUST surface as Result.Failure (not throw) so the
        // Planning consumer's existing failure-handling path (mark Job +
        // Items Failed → RecomputeStatusFromItems → escalate to Failed)
        // runs coherently. Throwing would crash the MassTransit consumer
        // and trigger a redelivery loop on a permanently-broken message.
        var strategy = CreateStrategy();
        var request = new DispatchGroupRequest(
            DeliveryOrderId: Guid.NewGuid(),
            GroupIndex: 1,
            PickupStationId: Guid.Empty,    // Manual: nulls collapsed to Empty
            DropStationId: Guid.Empty,
            UpperKey: "upper-G1",
            JobId: null);

        var result = await strategy.DispatchGroupAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        // Message names Phase 4 so audit readers know exactly what's missing.
        result.Error.Should().Contain("Phase 4");
        result.Error.Should().Contain("Manual transport mode");
    }

    [Fact]
    public async Task DispatchGroupAsync_DoesNotThrow_OnAnyInput()
    {
        // Defensive: even with empty / garbage inputs the stub returns
        // a typed failure. We don't want the stub to be the source of
        // an exception that crashes the consumer's group-iteration loop.
        var strategy = CreateStrategy();
        var request = new DispatchGroupRequest(
            DeliveryOrderId: Guid.Empty,
            GroupIndex: 0,
            PickupStationId: Guid.Empty,
            DropStationId: Guid.Empty,
            UpperKey: "",
            JobId: null);

        var act = async () => await strategy.DispatchGroupAsync(request, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
