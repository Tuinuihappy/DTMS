using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Facility.Application.Commands.CreateWarehouse;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Facility.UnitTests;

// Phase 2.7a — CreateWarehouseCommandHandler tests. The aggregate
// itself is heavily covered in WarehouseTests; here we pin the
// handler-level concerns:
//   - Value object composition from flat HTTP fields
//   - Code-duplicate fast-fail
//   - Geofence radius / polygon mutual exclusion at the command layer
//   - Contact partial-input (both required or neither)
//   - ArgumentException from VO ctors → Result.Failure (not 500)
public class CreateWarehouseCommandHandlerTests
{
    private readonly IWarehouseRepository _repo = Substitute.For<IWarehouseRepository>();
    private readonly CreateWarehouseCommandHandler _sut;

    public CreateWarehouseCommandHandlerTests()
    {
        _sut = new CreateWarehouseCommandHandler(_repo);
    }

    [Fact]
    public async Task Handle_ValidInput_CreatesAndReturnsId()
    {
        var command = NewValidCommand();

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        await _repo.Received(1).AddAsync(
            Arg.Is<Warehouse>(w =>
                w.Code == "WH-BKK-01" &&
                w.Name == "Bangkok DC" &&
                w.Location.Lat == 13.7 &&
                w.Location.Lng == 100.5),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsFailureWithoutWriting()
    {
        // Fast-fail check — the DB has a unique constraint as the
        // authoritative guard, but doing the read here gives a clean
        // "already exists" message instead of a confusing UniqueViolation
        // exception bubble.
        _repo.ResolveByCodeAsync("WH-BKK-01", Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var result = await _sut.Handle(NewValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_BothGeofenceFields_ReturnsFailure()
    {
        var command = NewValidCommand() with
        {
            GeofenceRadiusM = 100,
            GeofenceAreaWkt = "POLYGON((0 0, 1 0, 1 1, 0 1, 0 0))"
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("mutually exclusive");
    }

    [Fact]
    public async Task Handle_OnlyContactName_ReturnsFailure_BothRequired()
    {
        var command = NewValidCommand() with { ContactName = "Som", ContactPhone = null };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Contact requires both");
    }

    [Fact]
    public async Task Handle_BothContactFields_ComposesContact()
    {
        var command = NewValidCommand() with
        {
            ContactName = "Som",
            ContactPhone = "+66 89 123 4567",
            ContactEmail = "som@warehouse.example"
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<Warehouse>(w =>
                w.PrimaryContact != null &&
                w.PrimaryContact.Name == "Som" &&
                w.PrimaryContact.Phone == "+66 89 123 4567"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GeofenceRadius_StoresRadius()
    {
        var command = NewValidCommand() with { GeofenceRadiusM = 100 };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<Warehouse>(w => w.GeofenceRadiusM == 100 && w.GeofenceAreaWkt == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidLatLng_ReturnsFailure_FromVoValidation()
    {
        // The LatLng value object throws on out-of-range; the handler
        // catches and converts to Result.Failure so the HTTP layer
        // returns 400 with the message instead of 500.
        var command = NewValidCommand() with { Lat = 200 };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Latitude");
    }

    [Fact]
    public async Task Handle_DefaultsServiceModesToAmr_WhenNotSupplied()
    {
        // Backward-compatible default — pre-Phase-4 callers didn't think
        // about service modes; every warehouse historically served AMR.
        var command = NewValidCommand() with { ServiceModes = null };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<Warehouse>(w => w.ServesMode(TransportMode.Amr)),
            Arg.Any<CancellationToken>());
    }

    private static CreateWarehouseCommand NewValidCommand() => new(
        Code: "WH-BKK-01",
        Name: "Bangkok DC",
        Lat: 13.7,
        Lng: 100.5,
        AddressStreet: "123 Sukhumvit Rd",
        AddressCity: "Bangkok",
        ServiceModes: new[] { TransportMode.Amr });
}
