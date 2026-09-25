using DTMS.Dispatch.Application.Commands.SourceSystemTrip;
using DTMS.Dispatch.Domain.Entities;
using DTMS.SharedKernel.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UnitTests;

// The guard answers three different questions and they used to collapse into
// one 400, which told a partner nothing about whether to fix the id, fix its
// token, or call us. Exercised through the cancel handler because the
// authorizer itself is internal to the Application assembly.
public class SourceTripOriginAuthorizerTests
{
    [Fact]
    public async Task UnknownTrip_IsNotFound()
    {
        var trip = NewTrip();
        var handler = HandlerFor(trip, ownerKey: "oms");

        // A trip id the repository does not know.
        var act = () => handler.Handle(
            new SourceCancelTripCommand(Guid.NewGuid(), "oms", "oms-user-42", "x"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ForeignTrip_IsForbidden_AndNamesTheOwner()
    {
        var trip = NewTrip();
        var handler = HandlerFor(trip, ownerKey: "sap");

        var act = () => handler.Handle(
            new SourceCancelTripCommand(trip.Id, "oms", "oms-user-42", "x"), default);

        (await act.Should().ThrowAsync<ForbiddenException>()).WithMessage("*sap*");
    }

    [Fact]
    public async Task MissingParentOrder_IsAServerFault_NotANotFound()
    {
        // A trip whose order has vanished is our data being wrong. Answering 404
        // would tell the caller they sent a bad id, which is not what happened.
        var trip = NewTrip();
        var handler = HandlerFor(trip, ownerKey: null);

        var act = () => handler.Handle(
            new SourceCancelTripCommand(trip.Id, "oms", "oms-user-42", "x"), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await act.Should().NotThrowAsync<NotFoundException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static SourceCancelTripCommandHandler HandlerFor(Trip trip, string? ownerKey)
        => new(
            new FakeTripRepository(trip),
            new StubOrderStatusReader(null, ownerKey),
            new StubCancellationService(),
            NullLogger<SourceCancelTripCommandHandler>.Instance);

    private static Trip NewTrip()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}
