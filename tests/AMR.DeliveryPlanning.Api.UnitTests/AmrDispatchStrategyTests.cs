using AMR.DeliveryPlanning.Api.Adapters;
using DTMS.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Planning.Application.Services;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AMR.DeliveryPlanning.Api.UnitTests;

// Phase 3c — AmrDispatchStrategy is the production AMR entry point.
// The Planning consumer resolves it through IDispatchStrategyRegistry
// and invokes DispatchGroupAsync. This wraps the existing
// IDispatchOrderTemplateService.DispatchByRouteAsync 1:1 — the strategy
// adds the abstraction layer without changing AMR semantics. Tests pin:
//   - Mode is Amr (registry indexing key)
//   - Implements IDispatchStrategy
//   - Delegates inputs to the planning service
//   - Maps DispatchTemplateResult → DispatchGroupOutcome correctly
//   - Surfaces a failure from planning as a Result failure (not throw)
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
    public async Task DispatchGroupAsync_DelegatesToPlanning_AndMapsResult()
    {
        // The strategy's job is to translate the strategy contract into
        // the planning service contract. Anything beyond pass-through +
        // result mapping would be wrong — Planning owns OrderTemplate
        // resolution, RIOT3 dispatch, and Trip persistence.
        var orderId = Guid.NewGuid();
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        _planning.DispatchByRouteAsync(
            orderId, pickup, drop, "upper-G1",
            attemptNumber: 1,
            previousAttemptId: null,
            priorityOverride: null,
            appointVehicleKeyOverride: null,
            appointVehicleNameOverride: null,
            appointVehicleGroupKeyOverride: null,
            appointVehicleGroupNameOverride: null,
            appointQueueWaitAreaOverride: null,
            jobId: jobId,
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Result<DispatchTemplateResult>.Success(new DispatchTemplateResult(
                OrderTemplateId: Guid.NewGuid(),
                TemplateName: "PICK_THEN_DROP",
                VendorOrderKey: "RIOT3-XYZ",
                TripId: tripId,
                Resolved: null!)));

        var strategy = CreateStrategy();
        var result = await strategy.DispatchGroupAsync(
            new DispatchGroupRequest(orderId, GroupIndex: 1, pickup, drop, "upper-G1", jobId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TripId.Should().Be(tripId);
        result.Value.VendorOrderKey.Should().Be("RIOT3-XYZ");
        result.Value.TemplateName.Should().Be("PICK_THEN_DROP");
    }

    [Fact]
    public async Task DispatchGroupAsync_PlanningFailure_ReturnsFailure()
    {
        // Planning surfaces "no OrderTemplate registered" / vendor errors
        // as Result.Failure. The strategy must preserve them — turning
        // them into exceptions would make the Planning consumer's
        // structured failure handling (mark Job + Items Failed) collapse
        // into a generic catch.
        _planning.DispatchByRouteAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            attemptNumber: Arg.Any<int>(),
            previousAttemptId: Arg.Any<Guid?>(),
            priorityOverride: Arg.Any<int?>(),
            appointVehicleKeyOverride: Arg.Any<string?>(),
            appointVehicleNameOverride: Arg.Any<string?>(),
            appointVehicleGroupKeyOverride: Arg.Any<string?>(),
            appointVehicleGroupNameOverride: Arg.Any<string?>(),
            appointQueueWaitAreaOverride: Arg.Any<string?>(),
            jobId: Arg.Any<Guid?>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Result<DispatchTemplateResult>.Failure("No active OrderTemplate for route X → Y"));

        var strategy = CreateStrategy();
        var result = await strategy.DispatchGroupAsync(
            new DispatchGroupRequest(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "upper-X", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("OrderTemplate");
    }
}
