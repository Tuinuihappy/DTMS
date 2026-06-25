using AMR.DeliveryPlanning.Api.Adapters;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AMR.DeliveryPlanning.Api.UnitTests;

// Phase 4.4 — Real ManualDispatchStrategy. Pins the contract that the
// Planning consumer depends on:
//   - Mode is Manual (registry indexing key)
//   - No eligible operator → Failure (consumer marks Job/Items Failed)
//   - Happy path → Trip persisted + ManualTripExtension created +
//     Operator.AssignToTrip called + push fired
//   - Push failure does NOT fail the dispatch (best-effort)
//   - Upstream SLA, if tighter than computed drop deadline, wins
public class ManualDispatchStrategyTests
{
    private readonly IOperatorAssignmentPolicy _policy = Substitute.For<IOperatorAssignmentPolicy>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly IOperatorRepository _operators = Substitute.For<IOperatorRepository>();
    private readonly IPushNotificationGateway _push = Substitute.For<IPushNotificationGateway>();

    private ManualDispatchStrategy CreateStrategy(ManualDispatchOptions? opts = null) =>
        new(_policy, _trips, _extensions, _operators, _push,
            Options.Create(opts ?? new ManualDispatchOptions()),
            NullLogger<ManualDispatchStrategy>.Instance);

    private static DispatchGroupRequest BasicRequest(Guid? pickupWh = null) =>
        new(
            DeliveryOrderId: Guid.NewGuid(),
            GroupIndex: 1,
            PickupStationId: Guid.Empty,
            DropStationId: Guid.Empty,
            UpperKey: "UK-MAN-1",
            JobId: Guid.NewGuid(),
            PickupWarehouseId: pickupWh ?? Guid.NewGuid(),
            DropWarehouseId: Guid.NewGuid());

    [Fact]
    public void Mode_IsManual()
    {
        CreateStrategy().Mode.Should().Be(TransportMode.Manual);
    }

    [Fact]
    public void Implements_IDispatchStrategy()
    {
        CreateStrategy().Should().BeAssignableTo<IDispatchStrategy>();
    }

    [Fact]
    public async Task Dispatch_NoOperatorAvailable_ReturnsFailure_NoSideEffects()
    {
        _policy.SelectOperatorAsync(Arg.Any<OperatorAssignmentContext>(), Arg.Any<CancellationToken>())
               .Returns(OperatorAssignmentResult.NoMatch("No active + idle operator found."));
        var sut = CreateStrategy();

        var result = await sut.DispatchGroupAsync(BasicRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No active + idle operator");
        await _trips.DidNotReceive().AddAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>());
        await _extensions.DidNotReceive().AddAsync(Arg.Any<ManualTripExtension>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_DisabledByOption_ReturnsFailure()
    {
        var sut = CreateStrategy(new ManualDispatchOptions { EnableDispatch = false });
        var result = await sut.DispatchGroupAsync(BasicRequest(), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task Dispatch_HappyPath_PersistsTripExtensionAndAssignsOperator()
    {
        var op = Operator.CreateFromJwtClaims("EMP-700", "Driver", OperatorRole.Operator);
        _policy.SelectOperatorAsync(Arg.Any<OperatorAssignmentContext>(), Arg.Any<CancellationToken>())
               .Returns(OperatorAssignmentResult.Assigned(op));
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _push.SendToOperatorAsync(op.Id, Arg.Any<PushNotificationPayload>(), Arg.Any<CancellationToken>())
             .Returns(new PushFanoutResult(1, 0, Array.Empty<PushDeliveryOutcome>()));

        var sut = CreateStrategy();
        var request = BasicRequest();

        var result = await sut.DispatchGroupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TripId.Should().NotBe(Guid.Empty);
        result.Value.VendorOrderKey.Should().BeNull();   // Manual has no external key
        result.Value.TemplateName.Should().Be("manual");

        op.CurrentTripId.Should().Be(result.Value.TripId);

        await _trips.Received(1).AddAsync(
            Arg.Is<Trip>(t => t.UpperKey == "UK-MAN-1"
                           && t.DeliveryOrderId == request.DeliveryOrderId
                           && t.PickupWarehouseId == request.PickupWarehouseId),
            Arg.Any<CancellationToken>());
        await _extensions.Received(1).AddAsync(
            Arg.Is<ManualTripExtension>(e => e.OperatorId == op.Id),
            Arg.Any<CancellationToken>());
        await _operators.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_PushFailure_StillReturnsSuccess()
    {
        var op = Operator.CreateFromJwtClaims("EMP-701", "Driver", OperatorRole.Operator);
        _policy.SelectOperatorAsync(Arg.Any<OperatorAssignmentContext>(), Arg.Any<CancellationToken>())
               .Returns(OperatorAssignmentResult.Assigned(op));
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _push.SendToOperatorAsync(op.Id, Arg.Any<PushNotificationPayload>(), Arg.Any<CancellationToken>())
             .Returns<PushFanoutResult>(_ => throw new InvalidOperationException("push gateway down"));

        var sut = CreateStrategy();
        var result = await sut.DispatchGroupAsync(BasicRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Dispatch_StampsAllThreeSlaDeadlinesOnExtension()
    {
        var op = Operator.CreateFromJwtClaims("EMP-702", "Driver", OperatorRole.Operator);
        _policy.SelectOperatorAsync(Arg.Any<OperatorAssignmentContext>(), Arg.Any<CancellationToken>())
               .Returns(OperatorAssignmentResult.Assigned(op));
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);

        ManualTripExtension? capturedExtension = null;
        await _extensions.AddAsync(
            Arg.Do<ManualTripExtension>(e => capturedExtension = e),
            Arg.Any<CancellationToken>());

        var sut = CreateStrategy(new ManualDispatchOptions
        {
            AckSlaMinutes = 2,
            PickupSlaMinutes = 10,
            DropSlaMinutes = 60,
        });

        await sut.DispatchGroupAsync(BasicRequest(), CancellationToken.None);

        capturedExtension.Should().NotBeNull();
        capturedExtension!.AckDeadline.Should().NotBeNull();
        capturedExtension.PickupDeadline.Should().NotBeNull();
        capturedExtension.DropDeadline.Should().NotBeNull();
        // PickupDeadline = ack + pickup window from request time.
        (capturedExtension.PickupDeadline! - capturedExtension.AckDeadline!).Value
            .Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispatch_UpstreamSlaShorterThanComputedDropDeadline_UsesUpstream()
    {
        var op = Operator.CreateFromJwtClaims("EMP-703", "Driver", OperatorRole.Operator);
        _policy.SelectOperatorAsync(Arg.Any<OperatorAssignmentContext>(), Arg.Any<CancellationToken>())
               .Returns(OperatorAssignmentResult.Assigned(op));
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);

        ManualTripExtension? capturedExtension = null;
        await _extensions.AddAsync(
            Arg.Do<ManualTripExtension>(e => capturedExtension = e),
            Arg.Any<CancellationToken>());

        var tightSla = DateTime.UtcNow.AddMinutes(20);   // tighter than 5+30+120 default
        var request = BasicRequest() with { SlaDeadline = tightSla };

        var sut = CreateStrategy();
        await sut.DispatchGroupAsync(request, CancellationToken.None);

        capturedExtension!.DropDeadline.Should().Be(tightSla);
    }
}
