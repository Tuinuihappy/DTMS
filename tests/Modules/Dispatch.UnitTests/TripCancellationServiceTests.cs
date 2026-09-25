using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UnitTests;

// Vendor-first persistence semantics for the one cancel implementation.
// The service calls the vendor BEFORE persisting the local transition: if the
// vendor rejects, UpdateAsync must NOT be called (the in-memory mutation is
// discarded by scope disposal); if the domain rejects the transition, the
// vendor must NOT be called at all.
//
// Shares FakeTripRepository / StubVendorOps with UnitTest1.cs — same namespace,
// same assembly. Moved out of that file when the cancel logic moved out of
// CancelTripCommandHandler into TripCancellationService.

public class TripCancellationServiceTests
{
    [Fact]
    public async Task VendorSuccess_PersistsCancelledState()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var service = new TripCancellationService(repo, vendor, NullLogger<TripCancellationService>.Instance);
        var result = await service.CancelAsync(trip.Id, "ops");

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        // Handler routes envelope ops through vendorOrderKey not upperKey
        // (per commit 107675e — RIOT3 silently no-ops upperKey route).
        vendor.CancelCalls.Should().ContainSingle().Which.Should().Be(trip.VendorOrderKey);
    }

    [Fact]
    public async Task VendorFailure_DoesNotPersist()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextError = "network blip" };

        var service = new TripCancellationService(repo, vendor, NullLogger<TripCancellationService>.Instance);
        var result = await service.CancelAsync(trip.Id, "ops");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("network blip");
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task DomainReject_DoesNotCallVendor()
    {
        var trip = NewTripInProgress();
        trip.Cancel("first"); // already cancelled
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var service = new TripCancellationService(repo, vendor, NullLogger<TripCancellationService>.Instance);
        var result = await service.CancelAsync(trip.Id, "second");

        result.IsFailure.Should().BeTrue();
        vendor.CancelCalls.Should().BeEmpty();
        repo.UpdateCalls.Should().Be(0);
    }

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}
