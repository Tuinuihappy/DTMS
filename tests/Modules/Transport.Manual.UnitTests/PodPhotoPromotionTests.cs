using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;
using DTMS.Transport.Manual.Application.Commands.RecordPickup;
using DTMS.Transport.Manual.Application.Options;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using DTMS.Wms.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Transport.Manual.UnitTests;

// A POD upload is not a proof of delivery until a leg references it. Before
// staging, one written and never referenced — a retaken photo, an abandoned
// leg — sat under a final key forever, and 12 of the 13 objects in the bucket
// are exactly that. These tests hold the promotion step in place.
public class PodPhotoPromotionTests
{
    private const string Bucket = "dtms-pod";

    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IWmsLocationRepository _wmsLocations = Substitute.For<IWmsLocationRepository>();
    private readonly IGeofenceOverrideRequestRepository _overrides = Substitute.For<IGeofenceOverrideRequestRepository>();
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly IStorageBuckets _buckets = Substitute.For<IStorageBuckets>();

    private readonly Guid _tripId = Guid.NewGuid();
    private readonly Guid _operatorId = Guid.NewGuid();

    private ManualTripExtension GivenAcknowledgedTrip()
    {
        var ext = ManualTripExtension.AssignToOperator(_tripId, _operatorId, null, null);
        ext.MarkAcknowledged();
        _extensions.GetByTripIdAsync(_tripId, Arg.Any<CancellationToken>()).Returns(ext);
        _trips.GetByIdAsync(_tripId, Arg.Any<CancellationToken>())
              .Returns(Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1"));
        _buckets.Pod.Returns(Bucket);
        return ext;
    }

    private RecordPickupCommandHandler Sut() =>
        new(_extensions, _trips, _wmsLocations, _overrides,
            Options.Create(new RecordDropGeofenceOptions { Enabled = false }),
            _storage, _buckets, NullLogger<RecordPickupCommandHandler>.Instance);

    private Task<Result> Record(string? podKey) =>
        Sut().Handle(new RecordPickupCommand(_tripId, _operatorId, null, null, podKey), default);

    [Fact]
    public async Task StagedPhoto_IsPromotedAndTheFinalKeyIsStored()
    {
        var ext = GivenAcknowledgedTrip();
        var staged = PodObjectKey.GenerateStaging(_tripId, PodObjectKey.KindPickup);
        var expectedFinal = staged["incoming/".Length..];
        _storage.ObjectExistsAsync(Bucket, staged, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Record(staged);

        result.IsSuccess.Should().BeTrue();
        // The row must never point into the staging prefix — a lifecycle rule
        // expires everything there, so it would delete a live proof of delivery.
        ext.PickupPodKey.Should().Be(expectedFinal);
        await _storage.Received(1).CopyAsync(Bucket, staged, expectedFinal, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StagedPhotoThatIsGone_RecordsTheLegWithoutIt()
    {
        // The capture was abandoned, or the action waited in the offline queue
        // past the staging expiry. Retrying cannot bring the bytes back, and
        // refusing would strand an operator who has left the location.
        var ext = GivenAcknowledgedTrip();
        var staged = PodObjectKey.GenerateStaging(_tripId, PodObjectKey.KindPickup);
        _storage.ObjectExistsAsync(Bucket, staged, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Record(staged);

        result.IsSuccess.Should().BeTrue();
        ext.PickedUpAt.Should().NotBeNull();
        ext.PickupPodKey.Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task CopyFailure_RefusesTheLegSoTheOperatorCanRetry()
    {
        // Opposite of the case above: the photo is there and storage is having
        // a moment. Recording now would discard a proof that still exists.
        GivenAcknowledgedTrip();
        var staged = PodObjectKey.GenerateStaging(_tripId, PodObjectKey.KindPickup);
        _storage.ObjectExistsAsync(Bucket, staged, Arg.Any<CancellationToken>()).Returns(true);
        _storage.CopyAsync(Bucket, staged, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new IOException("storage down"));

        var result = await Record(staged);

        result.IsFailure.Should().BeTrue();
        await _extensions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherTripsKey_IsRefused()
    {
        // PodObjectKey has documented this check since it was written and
        // nothing called it, so the key was a caller-supplied string written
        // verbatim onto a proof of delivery.
        GivenAcknowledgedTrip();
        var someoneElses = PodObjectKey.GenerateStaging(Guid.NewGuid(), PodObjectKey.KindPickup);

        var result = await Record(someoneElses);

        result.IsFailure.Should().BeTrue();
        await _extensions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyForTheOtherLeg_IsRefused()
    {
        GivenAcknowledgedTrip();
        var dropKey = PodObjectKey.GenerateStaging(_tripId, PodObjectKey.KindDrop);

        var result = await Record(dropKey);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AlreadyFinalKey_IsTakenAsIs()
    {
        // A URL presigned just before this shipped and submitted just after.
        // Tolerated rather than failing a leg over a deploy boundary.
        var ext = GivenAcknowledgedTrip();
        var final = PodObjectKey.Generate(_tripId, PodObjectKey.KindPickup);

        var result = await Record(final);

        result.IsSuccess.Should().BeTrue();
        ext.PickupPodKey.Should().Be(final);
        await _storage.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task NoPhoto_StaysAnAcceptedOutcome()
    {
        var ext = GivenAcknowledgedTrip();

        var result = await Record(null);

        result.IsSuccess.Should().BeTrue();
        ext.PickedUpAt.Should().NotBeNull();
        ext.PickupPodKey.Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().ObjectExistsAsync(default!, default!, default);
    }
}
