using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.CreateWarehouse;

internal sealed class CreateWarehouseCommandHandler : ICommandHandler<CreateWarehouseCommand, Guid>
{
    private readonly IWarehouseRepository _repository;

    public CreateWarehouseCommandHandler(IWarehouseRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        // Code uniqueness — global. Checked here as a fast-fail to give
        // a nice "Code already exists" error; the DB unique index is the
        // authoritative guard against races (caught in the AddAsync try/catch
        // below if two writers collide).
        var existingId = await _repository.ResolveByCodeAsync(request.Code, cancellationToken);
        if (existingId.HasValue)
            return Result<Guid>.Failure($"Warehouse with code '{request.Code}' already exists.");

        // Compose value objects. Each throws ArgumentException on invalid
        // input (per the Phase 2.1 VO contracts) — we let those bubble
        // up so the API layer converts to 400 with the original message.
        Warehouse warehouse;
        try
        {
            var location = new LatLng(request.Lat, request.Lng);
            var address = new Address(
                request.AddressStreet,
                request.AddressCity,
                request.AddressState,
                request.AddressPostalCode,
                request.AddressCountry);

            ContactInfo? contact = null;
            if (!string.IsNullOrWhiteSpace(request.ContactName) ||
                !string.IsNullOrWhiteSpace(request.ContactPhone))
            {
                // ContactInfo requires both Name + Phone — caller must
                // supply both or neither. The aggregate-level helper
                // converts that partial-input case to a clear error.
                if (string.IsNullOrWhiteSpace(request.ContactName) ||
                    string.IsNullOrWhiteSpace(request.ContactPhone))
                {
                    return Result<Guid>.Failure(
                        "Contact requires both Name and Phone (or neither).");
                }
                contact = new ContactInfo(request.ContactName, request.ContactPhone, request.ContactEmail);
            }

            // ServiceModes default — keeps backward-compatible behaviour
            // for callers that don't know about Manual/Fleet yet.
            var modes = request.ServiceModes ?? new[] { TransportMode.Amr };

            warehouse = Warehouse.Create(
                code: request.Code,
                name: request.Name,
                location: location,
                address: address,
                serviceModes: modes,
                primaryContact: contact);

            // Apply optional geofence — domain enforces radius XOR polygon.
            if (request.GeofenceRadiusM.HasValue && !string.IsNullOrWhiteSpace(request.GeofenceAreaWkt))
                return Result<Guid>.Failure(
                    "GeofenceRadiusM and GeofenceAreaWkt are mutually exclusive — provide one or neither.");

            if (request.GeofenceRadiusM.HasValue)
                warehouse.SetGeofenceRadius(request.GeofenceRadiusM.Value);
            else if (!string.IsNullOrWhiteSpace(request.GeofenceAreaWkt))
                warehouse.SetGeofencePolygon(request.GeofenceAreaWkt);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        await _repository.AddAsync(warehouse, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(warehouse.Id);
    }
}
