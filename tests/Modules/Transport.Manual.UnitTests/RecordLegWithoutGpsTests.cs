using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Storage;
using DTMS.Transport.Manual.Application.Commands.RecordDrop;
using DTMS.Transport.Manual.Application.Commands.RecordPickup;
using DTMS.Transport.Manual.Application.Options;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using DTMS.Wms.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Transport.Manual.UnitTests;

// Wms:Geofence:Enabled is the only thing that decides whether an operator's
// coordinates are required, and it lives here — the browser cannot read it.
// The operator PWA therefore sends whatever fix it managed to get, null
// included, and lets these handlers answer. That contract is what these tests
// hold in place: a tablet on an HTTP LAN address gets PERMISSION_DENIED from
// Chromium no matter what the permission says, so if "fence off" ever stopped
// accepting a null fix, pickup would become unreachable on the devices this
// app exists for.
public class RecordLegWithoutGpsTests
{
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IWmsLocationRepository _wmsLocations = Substitute.For<IWmsLocationRepository>();
    private readonly IGeofenceOverrideRequestRepository _overrides = Substitute.For<IGeofenceOverrideRequestRepository>();
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly IStorageBuckets _buckets = Substitute.For<IStorageBuckets>();

    private readonly Guid _tripId = Guid.NewGuid();
    private readonly Guid _operatorId = Guid.NewGuid();

    private ManualTripExtension GivenAssignedTrip()
    {
        var ext = ManualTripExtension.AssignToOperator(_tripId, _operatorId, null, null);
        // The entity refuses a pickup before an acknowledgement, so every case
        // here starts from a leg that is legitimately ready to be recorded.
        ext.MarkAcknowledged();
        _extensions.GetByTripIdAsync(_tripId, Arg.Any<CancellationToken>()).Returns(ext);
        _trips.GetByIdAsync(_tripId, Arg.Any<CancellationToken>())
              .Returns(Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1"));
        return ext;
    }

    private RecordPickupCommandHandler Pickup(bool geofenceEnabled) =>
        new(_extensions, _trips, _wmsLocations, _overrides,
            Options.Create(new RecordDropGeofenceOptions { Enabled = geofenceEnabled }),
            _storage, _buckets, NullLogger<RecordPickupCommandHandler>.Instance);

    private RecordDropCommandHandler Drop(bool geofenceEnabled) =>
        new(_extensions, _trips, _wmsLocations, _overrides,
            Options.Create(new RecordDropGeofenceOptions { Enabled = geofenceEnabled }),
            _storage, _buckets, NullLogger<RecordDropCommandHandler>.Instance);

    [Fact]
    public async Task Pickup_FenceOff_AcceptsAMissingFix()
    {
        var ext = GivenAssignedTrip();

        var result = await Pickup(geofenceEnabled: false).Handle(
            new RecordPickupCommand(_tripId, _operatorId, null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        ext.PickedUpAt.Should().NotBeNull();
        // Never looked the location up, so a WMS snapshot gap cannot break a
        // leg that was not being fenced in the first place.
        await _wmsLocations.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Drop_FenceOff_AcceptsAMissingFix()
    {
        var ext = GivenAssignedTrip();
        ext.MarkPickedUp(podKey: null, overrideId: null);

        var result = await Drop(geofenceEnabled: false).Handle(
            new RecordDropCommand(_tripId, _operatorId, null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        ext.DroppedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Pickup_FenceOn_StillRefusesAMissingFix()
    {
        // The other half of the contract. Omitting coordinates must not be a
        // way around the fence, which is why the client cannot be trusted to
        // decide they are optional.
        GivenAssignedTrip();

        var result = await Pickup(geofenceEnabled: true).Handle(
            new RecordPickupCommand(_tripId, _operatorId, null, null, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("GEOFENCE_REQUIRED");
    }

    [Fact]
    public async Task Drop_FenceOn_StillRefusesAMissingFix()
    {
        GivenAssignedTrip();

        var result = await Drop(geofenceEnabled: true).Handle(
            new RecordDropCommand(_tripId, _operatorId, null, null, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("GEOFENCE_REQUIRED");
    }
}
