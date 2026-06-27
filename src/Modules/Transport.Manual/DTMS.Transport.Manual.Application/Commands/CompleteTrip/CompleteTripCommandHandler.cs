using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Commands.CompleteTrip;

internal sealed class CompleteTripCommandHandler : ICommandHandler<CompleteTripCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;
    private readonly IOperatorRepository _operators;

    public CompleteTripCommandHandler(
        IManualTripExtensionRepository extensions,
        ITripRepository trips,
        IOperatorRepository operators)
    {
        _extensions = extensions;
        _trips = trips;
        _operators = operators;
    }

    public async Task<Result> Handle(CompleteTripCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension.");
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure("Trip is assigned to a different operator.");
        if (ext.DroppedAt is null)
            return Result.Failure("Cannot complete — pickup/drop not recorded.");

        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result.Failure($"Trip {request.TripId} not found.");

        trip.MarkVendorCompleted();
        await _trips.UpdateAsync(trip, cancellationToken);

        // Free the operator for the next assignment.
        var op = await _operators.GetByIdAsync(request.OperatorId, cancellationToken);
        op?.ClearTripAssignment();
        if (op is not null) await _operators.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
