using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.UpdateStation;

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

        if (request.UpdateAction)
            station.SetActionConfig(request.ActionType, request.ActionCategory, request.ActionParameters);

        await _stationRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
