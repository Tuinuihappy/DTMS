using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.Admin.ReassignManualTrip;
using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class ReassignManualTripHandlerTests
{
    private readonly IManualTripExtensionRepository _extensions =
        Substitute.For<IManualTripExtensionRepository>();
    private readonly IOperatorRepository _operators =
        Substitute.For<IOperatorRepository>();
    private readonly IPushNotificationGateway _push =
        Substitute.For<IPushNotificationGateway>();

    private ReassignManualTripCommandHandler CreateSut() =>
        new(_extensions, _operators, _push,
            Options.Create(new ManualDispatchOptions()),
            NullLogger<ReassignManualTripCommandHandler>.Instance);

    private ManualTripExtension AssignedTo(Guid operatorId)
    {
        var ext = ManualTripExtension.AssignToOperator(
            Guid.NewGuid(), operatorId, null, null, null);
        _extensions.GetByTripIdAsync(ext.TripId, Arg.Any<CancellationToken>()).Returns(ext);
        return ext;
    }

    [Fact]
    public async Task Reassign_MissingExtension_Fails()
    {
        _extensions.GetByTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                   .Returns((ManualTripExtension?)null);
        var sut = CreateSut();

        var result = await sut.Handle(
            new ReassignManualTripCommand(Guid.NewGuid(), Guid.NewGuid(), null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no Manual extension");
    }

    [Fact]
    public async Task Reassign_SameOperator_Fails()
    {
        var opId = Guid.NewGuid();
        var ext = AssignedTo(opId);
        var sut = CreateSut();

        var result = await sut.Handle(
            new ReassignManualTripCommand(ext.TripId, opId, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already assigned");
    }

    [Fact]
    public async Task Reassign_TargetNotFound_Fails()
    {
        var ext = AssignedTo(Guid.NewGuid());
        var newId = Guid.NewGuid();
        _operators.GetByIdAsync(newId, Arg.Any<CancellationToken>()).Returns((Operator?)null);
        var sut = CreateSut();

        var result = await sut.Handle(
            new ReassignManualTripCommand(ext.TripId, newId, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Reassign_TargetOnLeave_Fails()
    {
        var ext = AssignedTo(Guid.NewGuid());
        var newOp = Operator.CreateFromJwtClaims("EMP-LEAVE", "On leave", OperatorRole.Operator);
        newOp.GoOnLeave("vacation");
        _operators.GetByIdAsync(newOp.Id, Arg.Any<CancellationToken>()).Returns(newOp);
        var sut = CreateSut();

        var result = await sut.Handle(
            new ReassignManualTripCommand(ext.TripId, newOp.Id, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("only Active");
    }

    [Fact]
    public async Task Reassign_TargetAlreadyBusy_Fails()
    {
        var ext = AssignedTo(Guid.NewGuid());
        var newOp = Operator.CreateFromJwtClaims("EMP-BUSY", "Busy", OperatorRole.Operator);
        newOp.AssignToTrip(Guid.NewGuid());   // already has a different trip
        _operators.GetByIdAsync(newOp.Id, Arg.Any<CancellationToken>()).Returns(newOp);
        var sut = CreateSut();

        var result = await sut.Handle(
            new ReassignManualTripCommand(ext.TripId, newOp.Id, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already has trip");
    }

    [Fact]
    public async Task Reassign_HappyPath_SwapsOperatorsAndPushes()
    {
        var oldOpId = Guid.NewGuid();
        var ext = AssignedTo(oldOpId);
        var oldOp = Operator.CreateFromJwtClaims("EMP-OLD", "Old", OperatorRole.Operator);
        oldOp.AssignToTrip(ext.TripId);
        _operators.GetByIdAsync(oldOpId, Arg.Any<CancellationToken>()).Returns(oldOp);
        var newOp = Operator.CreateFromJwtClaims("EMP-NEW", "New", OperatorRole.Operator);
        _operators.GetByIdAsync(newOp.Id, Arg.Any<CancellationToken>()).Returns(newOp);
        _push.SendToOperatorAsync(newOp.Id, Arg.Any<PushNotificationPayload>(), Arg.Any<CancellationToken>())
             .Returns(new PushFanoutResult(1, 0, Array.Empty<PushDeliveryOutcome>()));

        var sut = CreateSut();
        var result = await sut.Handle(
            new ReassignManualTripCommand(ext.TripId, newOp.Id, "load balance"), default);

        result.IsSuccess.Should().BeTrue();
        oldOp.CurrentTripId.Should().BeNull();
        newOp.CurrentTripId.Should().Be(ext.TripId);
        ext.OperatorId.Should().Be(newOp.Id);
        await _push.Received(1).SendToOperatorAsync(
            newOp.Id, Arg.Any<PushNotificationPayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reassign_PushFailure_StillSucceeds()
    {
        var oldOp = Operator.CreateFromJwtClaims("EMP-OLD2", "Old", OperatorRole.Operator);
        var ext = AssignedTo(oldOp.Id);
        oldOp.AssignToTrip(ext.TripId);
        _operators.GetByIdAsync(oldOp.Id, Arg.Any<CancellationToken>()).Returns(oldOp);
        var newOp = Operator.CreateFromJwtClaims("EMP-NEW2", "New", OperatorRole.Operator);
        _operators.GetByIdAsync(newOp.Id, Arg.Any<CancellationToken>()).Returns(newOp);
        _push.SendToOperatorAsync(newOp.Id, Arg.Any<PushNotificationPayload>(), Arg.Any<CancellationToken>())
             .Returns<PushFanoutResult>(_ => throw new InvalidOperationException("push gateway down"));

        var sut = CreateSut();
        var result = await sut.Handle(
            new ReassignManualTripCommand(ext.TripId, newOp.Id, null), default);

        result.IsSuccess.Should().BeTrue();
    }
}
