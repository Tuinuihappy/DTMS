using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.AcknowledgeTrip;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class AcknowledgeTripHandlerTests
{
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();

    [Fact]
    public async Task Handle_NoExtension_FailsWithMessage()
    {
        _extensions.GetByTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                   .Returns((ManualTripExtension?)null);
        var sut = new AcknowledgeTripCommandHandler(_extensions);

        var result = await sut.Handle(
            new AcknowledgeTripCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no Manual extension");
    }

    [Fact]
    public async Task Handle_DifferentOperator_Fails()
    {
        var tripId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, owner, null, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);
        var sut = new AcknowledgeTripCommandHandler(_extensions);

        var result = await sut.Handle(new AcknowledgeTripCommand(tripId, other), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("different operator");
    }

    [Fact]
    public async Task Handle_HappyPath_MarksAcknowledgedAndSaves()
    {
        var tripId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, owner, null, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);
        var sut = new AcknowledgeTripCommandHandler(_extensions);

        var result = await sut.Handle(new AcknowledgeTripCommand(tripId, owner), default);

        result.IsSuccess.Should().BeTrue();
        ext.AcknowledgedAt.Should().NotBeNull();
        await _extensions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
