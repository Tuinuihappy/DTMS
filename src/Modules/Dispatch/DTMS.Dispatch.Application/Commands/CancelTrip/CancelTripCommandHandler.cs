using DTMS.Dispatch.Application.Services;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.CancelTrip;

// Thin MediatR entry point over ITripCancellationService. The vendor call and
// the xmin conflict retry live in the service so the federated source-system
// cancel can reuse them without a nested Send — see TripCancellationService.
// Cross-module callers (OrderCancelledCascadeConsumer, BulkCancelTrips) keep
// coming in through this command.
public class CancelTripCommandHandler : ICommandHandler<CancelTripCommand>
{
    private readonly ITripCancellationService _cancellation;

    public CancelTripCommandHandler(ITripCancellationService cancellation)
        => _cancellation = cancellation;

    public Task<Result> Handle(CancelTripCommand request, CancellationToken cancellationToken)
        => _cancellation.CancelAsync(
            request.TripId, request.Reason, request.ActionBy, request.ActedAt,
            request.Origin, cancellationToken);
}
