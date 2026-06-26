using DTMS.Facility.Domain.Enums;
using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.ReleaseShelf;

public record ReleaseShelfCommand(string Rfid) : ICommand<bool>;

internal sealed class ReleaseShelfCommandHandler : ICommandHandler<ReleaseShelfCommand, bool>
{
    private readonly IShelfRepository _shelfRepository;

    public ReleaseShelfCommandHandler(IShelfRepository shelfRepository)
        => _shelfRepository = shelfRepository;

    public async Task<Result<bool>> Handle(ReleaseShelfCommand request, CancellationToken cancellationToken)
    {
        var shelf = await _shelfRepository.GetByRfidAsync(request.Rfid, cancellationToken);
        if (shelf is null)
            return Result<bool>.Failure($"Shelf '{request.Rfid}' not found.");

        if (shelf.Status == ShelfStatus.Available)
            return Result<bool>.Success(true);

        shelf.SetAvailable();
        await _shelfRepository.UpdateAsync(shelf, cancellationToken);
        await _shelfRepository.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
