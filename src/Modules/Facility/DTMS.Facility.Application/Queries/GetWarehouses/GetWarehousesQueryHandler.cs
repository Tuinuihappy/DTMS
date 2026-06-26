using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Queries.GetWarehouses;

internal sealed class GetWarehousesQueryHandler
    : IQueryHandler<GetWarehousesQuery, IReadOnlyList<WarehouseListItemDto>>
{
    private readonly IWarehouseRepository _repository;

    public GetWarehousesQueryHandler(IWarehouseRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<WarehouseListItemDto>>> Handle(
        GetWarehousesQuery request, CancellationToken cancellationToken)
    {
        var warehouses = await _repository.ListAsync(
            serviceMode: request.ServiceMode,
            excludeInactive: request.ExcludeInactive,
            cancellationToken: cancellationToken);

        var dtos = warehouses.Select(w => new WarehouseListItemDto(
            Id: w.Id,
            Code: w.Code,
            Name: w.Name,
            Lat: w.Location.Lat,
            Lng: w.Location.Lng,
            AddressStreet: w.Address.Street,
            AddressCity: w.Address.City,
            ServiceModes: w.ServiceModes.Select(m => m.ToString()).ToArray(),
            GeofenceRadiusM: w.GeofenceRadiusM,
            // Boolean flag instead of the full WKT — keeps the list payload
            // small (some polygons are 5KB). Frontend fetches the full
            // detail via GetWarehouseByIdQuery when actually editing the
            // geofence.
            HasGeofencePolygon: !string.IsNullOrEmpty(w.GeofenceAreaWkt),
            ContactName: w.PrimaryContact?.Name,
            ContactPhone: w.PrimaryContact?.Phone,
            IsActive: w.IsActive,
            CreatedAt: w.CreatedAt)).ToList();

        return Result<IReadOnlyList<WarehouseListItemDto>>.Success(dtos);
    }
}
