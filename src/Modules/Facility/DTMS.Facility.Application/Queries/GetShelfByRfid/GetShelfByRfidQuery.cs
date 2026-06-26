using DTMS.Facility.Domain.Enums;
using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Queries.GetShelfByRfid;

public record ShelfDto(
    Guid Id,
    Guid MapId,
    string Rfid,
    Guid? CurrentStationId,
    double MaxWeightKg,
    int MaxSlots,
    string Status);

public record GetShelfByRfidQuery(string Rfid) : IQuery<ShelfDto>;

public class GetShelfByRfidQueryHandler : IQueryHandler<GetShelfByRfidQuery, ShelfDto>
{
    private readonly IShelfRepository _shelfRepository;

    public GetShelfByRfidQueryHandler(IShelfRepository shelfRepository)
    {
        _shelfRepository = shelfRepository;
    }

    public async Task<Result<ShelfDto>> Handle(GetShelfByRfidQuery request, CancellationToken cancellationToken)
    {
        var shelf = await _shelfRepository.GetByRfidAsync(request.Rfid, cancellationToken);
        if (shelf is null)
            return Result<ShelfDto>.Failure($"Shelf with RFID '{request.Rfid}' not found.");

        return Result<ShelfDto>.Success(new ShelfDto(
            shelf.Id,
            shelf.MapId,
            shelf.Rfid,
            shelf.CurrentStationId,
            shelf.MaxWeightKg,
            shelf.MaxSlots,
            shelf.Status.ToString()));
    }
}
