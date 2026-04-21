using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskCompleted;

public class ReportTaskCompletedCommandHandler : ICommandHandler<ReportTaskCompletedCommand>
{
    private readonly ITripRepository _tripRepository;

    public ReportTaskCompletedCommandHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result> Handle(ReportTaskCompletedCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            trip.CompleteTask(request.TaskId);
            await _tripRepository.UpdateAsync(trip, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
