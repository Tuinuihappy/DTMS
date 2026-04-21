using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskFailed;

public class ReportTaskFailedCommandHandler : ICommandHandler<ReportTaskFailedCommand>
{
    private readonly ITripRepository _tripRepository;

    public ReportTaskFailedCommandHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result> Handle(ReportTaskFailedCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            trip.FailTask(request.TaskId, request.Reason);
            await _tripRepository.UpdateAsync(trip, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
