using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Amr.Consumers;
using DTMS.Transport.Amr.Services;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace VendorAdapter.UnitTests;

// Fix B: the terminal snapshot capture must ALSO backfill the vendor vehicle
// from the raw it already fetched. Capturing the snapshot flips
// VendorFinalSnapshot non-null, which drops the trip out of the reconciler's
// self-heal query (GetTerminalTripsMissingVehicleAsync gates on
// VendorFinalSnapshot == null). So if the consumer doesn't recover the robot
// here, the self-heal path never gets another chance and the vehicle is lost
// forever — exactly what happened to order 4411, whose robot RIOT3 reported
// only under executeVehicle*.
public class CaptureFinalSnapshotConsumerTests
{
    private static Trip TerminalTripMissingVehicle(string upperKey = "upper-G1")
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, "RIOT3-ABC");
        trip.MarkVendorStarted();     // Created → InProgress, NO vehicle captured
        trip.MarkVendorCompleted();   // → Completed, snapshot still null
        return trip;
    }

    private static ConsumeContext<TripCompletedIntegrationEvent> CompletedContext(Trip trip)
    {
        var ctx = Substitute.For<ConsumeContext<TripCompletedIntegrationEvent>>();
        ctx.Message.Returns(new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, trip.Id, Guid.NewGuid(), trip.DeliveryOrderId, trip.UpperKey!));
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_TerminalTripMissingVehicle_BackfillsFromExecuteVehicle_AndSealsSnapshot()
    {
        var trip = TerminalTripMissingVehicle();
        var repo = Substitute.For<ITripRepository>();
        repo.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        var query = Substitute.For<IRiot3OrderQueryService>();
        query.GetRawByUpperKeyAsync(trip.UpperKey!, Arg.Any<CancellationToken>())
            .Returns("{\"code\":\"0\",\"data\":{\"executeVehicleKey\":\"1ee96f0f\","
                     + "\"executeVehicleName\":\"FAN1_STANDARD_NO3\",\"finalTime\":\"2026-07-09T08:45:10Z\"}}");

        var consumer = new CaptureFinalSnapshotConsumer(
            repo, query, NullLogger<CaptureFinalSnapshotConsumer>.Instance);

        await consumer.Consume(CompletedContext(trip));

        trip.VendorVehicleKey.Should().Be("1ee96f0f");
        trip.VendorVehicleName.Should().Be("FAN1_STANDARD_NO3");
        trip.VendorFinalSnapshot.Should().NotBeNull();
        await repo.Received(1).UpdateAsync(trip, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_VendorRecordHasNoVehicle_SealsSnapshot_LeavesVehicleNull()
    {
        var trip = TerminalTripMissingVehicle();
        var repo = Substitute.For<ITripRepository>();
        repo.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        var query = Substitute.For<IRiot3OrderQueryService>();
        query.GetRawByUpperKeyAsync(trip.UpperKey!, Arg.Any<CancellationToken>())
            .Returns("{\"code\":\"0\",\"data\":{}}");

        var consumer = new CaptureFinalSnapshotConsumer(
            repo, query, NullLogger<CaptureFinalSnapshotConsumer>.Instance);

        await consumer.Consume(CompletedContext(trip));

        trip.VendorVehicleKey.Should().BeNull();
        trip.VendorFinalSnapshot.Should().NotBeNull();   // sealed anyway → no re-fetch loop
        await repo.Received(1).UpdateAsync(trip, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SnapshotAlreadyCaptured_SkipsVendorRefetch()
    {
        // First-writer-wins: the reconciler already sealed the trip, so the
        // consumer must not re-fetch from RIOT3 (and must not clobber).
        var trip = TerminalTripMissingVehicle();
        trip.CaptureFinalSnapshot("{\"data\":{}}", null);

        var repo = Substitute.For<ITripRepository>();
        repo.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);
        var query = Substitute.For<IRiot3OrderQueryService>();

        var consumer = new CaptureFinalSnapshotConsumer(
            repo, query, NullLogger<CaptureFinalSnapshotConsumer>.Instance);

        await consumer.Consume(CompletedContext(trip));

        await query.DidNotReceive().GetRawByUpperKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
