using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.RegisterShelf;

internal sealed class RegisterShelfCommandHandler : ICommandHandler<RegisterShelfCommand, Guid>
{
    private readonly IShelfRepository _shelfRepository;
    private readonly IMapRepository _mapRepository;

    public RegisterShelfCommandHandler(IShelfRepository shelfRepository, IMapRepository mapRepository)
    {
        _shelfRepository = shelfRepository;
        _mapRepository = mapRepository;
    }

    public async Task<Result<Guid>> Handle(RegisterShelfCommand request, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetByIdAsync(request.MapId, cancellationToken);
        if (map is null)
            return Result<Guid>.Failure($"Map {request.MapId} not found.");

        var existing = await _shelfRepository.GetByRfidAsync(request.Rfid, cancellationToken);
        if (existing is not null)
            return Result<Guid>.Failure($"Shelf with RFID '{request.Rfid}' already registered.");

        var shelf = new Shelf(request.MapId, request.Rfid, request.MaxWeightKg, request.MaxSlots);
        await _shelfRepository.AddAsync(shelf, cancellationToken);
        await _shelfRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(shelf.Id);
    }
}
