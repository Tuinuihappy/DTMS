using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.UpdateWarehouse;

internal sealed class UpdateWarehouseCommandHandler : ICommandHandler<UpdateWarehouseCommand>
{
    private readonly IWarehouseRepository _repository;

    public UpdateWarehouseCommandHandler(IWarehouseRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(UpdateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (warehouse is null)
            return Result.Failure($"Warehouse {request.Id} not found.");

        try
        {
            // Location + Address are atomic — both must be provided to
            // change either (Address without LatLng would leave the
            // geofence center pointing at the wrong place).
            if (request.Lat.HasValue || request.Lng.HasValue ||
                request.AddressStreet != null)
            {
                if (!request.Lat.HasValue || !request.Lng.HasValue || request.AddressStreet == null)
                    return Result.Failure("Lat, Lng, and AddressStreet must all be provided together.");

                var location = new LatLng(request.Lat.Value, request.Lng.Value);
                var address = new Address(
                    request.AddressStreet,
                    request.AddressCity,
                    request.AddressState,
                    request.AddressPostalCode,
                    request.AddressCountry);
                warehouse.UpdateLocation(location, address);
            }

            // Contact — partial update semantics:
            //   ContactName = null AND ContactPhone = null → don't touch
            //   ContactName = "" AND ContactPhone = "" → clear contact
            //   Either has value → set new contact (both required)
            if (request.ContactName != null || request.ContactPhone != null)
            {
                if (string.IsNullOrEmpty(request.ContactName) && string.IsNullOrEmpty(request.ContactPhone))
                {
                    warehouse.UpdateContact(null);
                }
                else if (string.IsNullOrWhiteSpace(request.ContactName) ||
                         string.IsNullOrWhiteSpace(request.ContactPhone))
                {
                    return Result.Failure("Contact requires both Name and Phone (or both empty to clear).");
                }
                else
                {
                    var contact = new ContactInfo(request.ContactName, request.ContactPhone, request.ContactEmail);
                    warehouse.UpdateContact(contact);
                }
            }

            // ServiceModes — replace the whole set if provided. Domain
            // enforces ≥1 mode (so empty array would throw at the diff loop).
            if (request.ServiceModes != null)
            {
                if (request.ServiceModes.Count == 0)
                    return Result.Failure("ServiceModes cannot be empty — use Deactivate to take a warehouse offline.");

                // Diff: enable new, disable removed. Each call is
                // idempotent so re-supplying the existing set is safe.
                var current = warehouse.ServiceModes.ToList();
                foreach (var mode in request.ServiceModes.Distinct())
                {
                    if (!current.Contains(mode))
                        warehouse.EnableServiceMode(mode);
                }
                foreach (var mode in current)
                {
                    if (!request.ServiceModes.Contains(mode))
                        warehouse.DisableServiceMode(mode);
                }
            }

            // Geofence — three states:
            //   ClearGeofence = true → clear both
            //   GeofenceRadiusM has value → set radius (auto-clears polygon)
            //   GeofenceAreaWkt provided → set polygon (auto-clears radius)
            //   All null + ClearGeofence false → no change
            if (request.ClearGeofence)
            {
                warehouse.SetGeofenceRadius(null);
            }
            else if (request.GeofenceRadiusM.HasValue && !string.IsNullOrWhiteSpace(request.GeofenceAreaWkt))
            {
                return Result.Failure(
                    "Cannot set both GeofenceRadiusM and GeofenceAreaWkt — they're mutually exclusive.");
            }
            else if (request.GeofenceRadiusM.HasValue)
            {
                warehouse.SetGeofenceRadius(request.GeofenceRadiusM.Value);
            }
            else if (!string.IsNullOrWhiteSpace(request.GeofenceAreaWkt))
            {
                warehouse.SetGeofencePolygon(request.GeofenceAreaWkt);
            }
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }

        _repository.Update(warehouse);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
