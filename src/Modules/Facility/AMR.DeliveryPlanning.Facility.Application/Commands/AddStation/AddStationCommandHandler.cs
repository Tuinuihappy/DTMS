using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.AddStation;

internal sealed class AddStationCommandHandler : ICommandHandler<AddStationCommand, Guid>
{
    private readonly IMapRepository _mapRepository;

    public AddStationCommandHandler(IMapRepository mapRepository)
    {
        _mapRepository = mapRepository;
    }

    public async Task<Result<Guid>> Handle(AddStationCommand request, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetByIdAsync(request.MapId, cancellationToken);
        if (map is null)
        {
            throw new NotFoundException($"Map with ID {request.MapId} not found.");
        }

        var coordinate = new Coordinate(request.X, request.Y, request.Theta);
        var station = new Station(Guid.NewGuid(), request.MapId, request.Name, coordinate, request.Type);
        if (!string.IsNullOrWhiteSpace(request.VendorRef))
            station.SetVendorRef(request.VendorRef.Trim());

        if (!string.IsNullOrWhiteSpace(request.Code))
            station.SetCode(request.Code);

        map.AddStation(station);

        _mapRepository.Update(map);
        await _mapRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(station.Id);
    }
}
