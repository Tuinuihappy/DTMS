using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetWarehouseById;

internal sealed class GetWarehouseByIdQueryHandler
    : IQueryHandler<GetWarehouseByIdQuery, WarehouseDetailDto>
{
    private readonly IWarehouseRepository _repository;

    public GetWarehouseByIdQueryHandler(IWarehouseRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<WarehouseDetailDto>> Handle(
        GetWarehouseByIdQuery request, CancellationToken cancellationToken)
    {
        var w = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (w is null)
            return Result<WarehouseDetailDto>.Failure($"Warehouse {request.Id} not found.");

        var dto = new WarehouseDetailDto(
            Id: w.Id,
            Code: w.Code,
            Name: w.Name,
            Lat: w.Location.Lat,
            Lng: w.Location.Lng,
            AddressStreet: w.Address.Street,
            AddressCity: w.Address.City,
            AddressState: w.Address.State,
            AddressPostalCode: w.Address.PostalCode,
            AddressCountry: w.Address.Country,
            ServiceModes: w.ServiceModes.Select(m => m.ToString()).ToArray(),
            GeofenceRadiusM: w.GeofenceRadiusM,
            GeofenceAreaWkt: w.GeofenceAreaWkt,
            ContactName: w.PrimaryContact?.Name,
            ContactPhone: w.PrimaryContact?.Phone,
            ContactEmail: w.PrimaryContact?.Email,
            Monday: ToDayDto(w.Hours.MondayOpen, w.Hours.MondayClose),
            Tuesday: ToDayDto(w.Hours.TuesdayOpen, w.Hours.TuesdayClose),
            Wednesday: ToDayDto(w.Hours.WednesdayOpen, w.Hours.WednesdayClose),
            Thursday: ToDayDto(w.Hours.ThursdayOpen, w.Hours.ThursdayClose),
            Friday: ToDayDto(w.Hours.FridayOpen, w.Hours.FridayClose),
            Saturday: ToDayDto(w.Hours.SaturdayOpen, w.Hours.SaturdayClose),
            Sunday: ToDayDto(w.Hours.SundayOpen, w.Hours.SundayClose),
            IsActive: w.IsActive,
            CreatedAt: w.CreatedAt,
            UpdatedAt: w.UpdatedAt);

        return Result<WarehouseDetailDto>.Success(dto);
    }

    // TimeSpan → "HH:mm" string. Null both ends = closed. The frontend
    // form treats "" as "set this day to closed" — matches what the
    // OperatingHours.AlwaysOpen factory generates for unused days.
    private static OperatingHoursDayDto ToDayDto(TimeSpan? open, TimeSpan? close) =>
        new(
            Open: open?.ToString(@"hh\:mm"),
            Close: close?.ToString(@"hh\:mm"));
}
