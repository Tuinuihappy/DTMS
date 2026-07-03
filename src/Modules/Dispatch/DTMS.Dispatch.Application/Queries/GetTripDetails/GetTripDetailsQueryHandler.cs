using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Operators;

namespace DTMS.Dispatch.Application.Queries.GetTripDetails;

public class GetTripDetailsQueryHandler : IQueryHandler<GetTripDetailsQuery, TripDetailsDto>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITripMissionEventRepository _missionRepository;
    private readonly IOperatorDirectory _operatorDirectory;

    public GetTripDetailsQueryHandler(
        ITripRepository tripRepository,
        ITripMissionEventRepository missionRepository,
        IOperatorDirectory operatorDirectory)
    {
        _tripRepository = tripRepository;
        _missionRepository = missionRepository;
        _operatorDirectory = operatorDirectory;
    }

    public async Task<Result<TripDetailsDto>> Handle(GetTripDetailsQuery request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<TripDetailsDto>.Failure($"Trip {request.TripId} not found.");

        var missions = await _missionRepository.GetByTripIdAsync(trip.Id, cancellationToken);

        // Manual pool trips have no vendor vehicle — resolve the claiming
        // operator's name so the drawer's "Vehicle / Operator" cell can show
        // who took the job. Skipped for AMR / unclaimed trips (Id is null).
        var claimedByOperatorName = trip.ClaimedByOperatorId is { } opId
            ? await _operatorDirectory.GetDisplayNameAsync(opId, cancellationToken)
            : null;

        var missionDtos = missions
            .Select(m => new TripMissionDto(
                MissionIndex: m.MissionIndex,
                MissionKey: m.MissionKey,
                MissionType: m.MissionType,
                State: m.State,
                StationName: m.StationName,
                ActionName: m.ActionName,
                ActionType: m.ActionType,
                ResultCode: m.ResultCode,
                ErrorMessage: m.ErrorMessage,
                ChangeStateTime: m.ChangeStateTime,
                ReceivedAt: m.ReceivedAt))
            .ToList();

        var dto = new TripDetailsDto(
            Id: trip.Id,
            DeliveryOrderId: trip.DeliveryOrderId,
            Status: trip.Status.ToString(),
            AttemptNumber: trip.AttemptNumber,
            PreviousAttemptId: trip.PreviousAttemptId,
            UpperKey: trip.UpperKey,
            VendorOrderKey: trip.VendorOrderKey,
            VendorVehicleKey: trip.VendorVehicleKey,
            VendorVehicleName: trip.VendorVehicleName,
            ClaimedByOperatorId: trip.ClaimedByOperatorId,
            ClaimedByOperatorName: claimedByOperatorName,
            TemplateNameAtDispatch: trip.TemplateNameAtDispatch,
            PriorityAtDispatch: trip.PriorityAtDispatch,
            CreatedAt: trip.CreatedAt,
            StartedAt: trip.StartedAt,
            CompletedAt: trip.CompletedAt,
            VendorExpectedCompletionAt: trip.VendorExpectedCompletionAt,
            FailureReason: trip.FailureReason,
            PickupStationId: trip.PickupStationId,
            DropStationId: trip.DropStationId,
            Missions: missionDtos,
            VendorRequestSnapshot: request.IncludeRawSnapshots ? trip.VendorRequestSnapshot : null,
            VendorFinalSnapshot: request.IncludeRawSnapshots ? trip.VendorFinalSnapshot : null);

        return Result<TripDetailsDto>.Success(dto);
    }
}
