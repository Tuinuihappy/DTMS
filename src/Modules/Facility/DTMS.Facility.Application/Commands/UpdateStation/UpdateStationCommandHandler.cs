using DTMS.Facility.Application.Commands;
using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.UpdateStation;

internal sealed class UpdateStationCommandHandler : ICommandHandler<UpdateStationCommand>
{
    private readonly IStationRepository _stationRepository;

    public UpdateStationCommandHandler(IStationRepository stationRepository)
    {
        _stationRepository = stationRepository;
    }

    public async Task<Result> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var station = await _stationRepository.GetByIdAsync(request.StationId, cancellationToken);
        if (station is null)
            throw new NotFoundException($"Station {request.StationId} not found.");

        if (request.Type.HasValue)
            station.SetType(request.Type.Value);

        if (request.Code is not null)
            station.SetCode(request.Code);

        if (request.UpdateActions)
            station.SetActions(StationActionInputMapper.ToDomain(request.Actions));

        await _stationRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
