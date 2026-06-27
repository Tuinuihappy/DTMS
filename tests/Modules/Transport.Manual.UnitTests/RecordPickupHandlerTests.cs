using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Domain.ValueObjects;
using DTMS.Transport.Manual.Application.Commands.RecordPickup;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class RecordPickupHandlerTests
{
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IWarehouseRepository _warehouses = Substitute.For<IWarehouseRepository>();
    private readonly IGeofenceOverrideRequestRepository _overrides = Substitute.For<IGeofenceOverrideRequestRepository>();

    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid PickupWarehouseId = Guid.NewGuid();

    private RecordPickupCommandHandler CreateSut() =>
        new(_extensions, _trips, _warehouses, _overrides);

    private void ArrangeAcknowledgedExtension()
    {
        var ext = ManualTripExtension.AssignToOperator(TripId, OperatorId, null, null, null);
        ext.MarkAcknowledged();
        _extensions.GetByTripIdAsync(TripId, Arg.Any<CancellationToken>()).Returns(ext);
    }

    private void ArrangeTrip()
    {
        var trip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(),
            upperKey: "UK-TEST",
            vendorOrderKey: null,
            pickupWarehouseId: PickupWarehouseId,
            dropWarehouseId: Guid.NewGuid());
        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>()).Returns(trip);
    }

    private void ArrangeWarehouse(int radiusM)
    {
        var warehouse = Warehouse.Create(
            code: "WH-TEST",
            name: "Test Warehouse",
            location: new LatLng(13.7500, 100.4914),
            address: new Address("1 Test Rd", "Bangkok", null, null, "Thailand"),
            serviceModes: new[] { TransportMode.Manual });
        warehouse.SetGeofenceRadius(radiusM);
        _warehouses.GetByIdAsync(PickupWarehouseId, Arg.Any<CancellationToken>()).Returns(warehouse);
    }

    [Fact]
    public async Task Handle_InsideGeofence_MarksPickedUp()
    {
        ArrangeAcknowledgedExtension();
        ArrangeTrip();
        ArrangeWarehouse(radiusM: 200);

        var sut = CreateSut();
        var result = await sut.Handle(new RecordPickupCommand(
            TripId, OperatorId,
            ReportedLat: 13.7500, ReportedLng: 100.4914,
            PodKey: "pod/123"), default);

        result.IsSuccess.Should().BeTrue();
        await _extensions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OutsideGeofence_NoOverride_Rejects()
    {
        ArrangeAcknowledgedExtension();
        ArrangeTrip();
        ArrangeWarehouse(radiusM: 50);
        // Trip.CreateForEnvelope assigns its own Guid Id, so match
        // any tripId here — operator+warehouse are the contract that
        // matters for the override lookup.
        _overrides.GetApprovedForTripLegAsync(Arg.Any<Guid>(), OperatorId, PickupWarehouseId, Arg.Any<CancellationToken>())
                  .Returns((GeofenceOverrideRequest?)null);

        var sut = CreateSut();
        var result = await sut.Handle(new RecordPickupCommand(
            TripId, OperatorId,
            ReportedLat: 13.7600, ReportedLng: 100.4914,  // ~1.1km north
            PodKey: null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("GEOFENCE_REJECTED");
        await _extensions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OutsideGeofence_WithApprovedOverride_RecordsAndStampsOverrideId()
    {
        ArrangeAcknowledgedExtension();
        ArrangeTrip();
        ArrangeWarehouse(radiusM: 50);

        var approved = GeofenceOverrideRequest.Submit(
            operatorId: OperatorId,
            tripId: TripId,
            expectedWarehouseId: PickupWarehouseId,
            reportedLat: 13.7600, reportedLng: 100.4914,
            distanceFromGeofenceM: 1100,
            reason: "GPS drift",
            photoUrl: null,
            expiresIn: TimeSpan.FromMinutes(10));
        approved.Approve(Guid.NewGuid());
        _overrides.GetApprovedForTripLegAsync(Arg.Any<Guid>(), OperatorId, PickupWarehouseId, Arg.Any<CancellationToken>())
                  .Returns(approved);

        var sut = CreateSut();
        var result = await sut.Handle(new RecordPickupCommand(
            TripId, OperatorId, 13.7600, 100.4914, "pod/override"), default);

        result.IsSuccess.Should().BeTrue();
        // Verify the override id was captured on the extension via the call to MarkPickedUp.
        var ext = await _extensions.GetByTripIdAsync(TripId, default);
        ext!.PickupGeofenceOverrideId.Should().Be(approved.Id);
        ext.PickupPodKey.Should().Be("pod/override");
    }
}
