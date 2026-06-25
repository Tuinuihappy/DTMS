using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.AcknowledgeTrip;

internal sealed class AcknowledgeTripCommandHandler : ICommandHandler<AcknowledgeTripCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    public AcknowledgeTripCommandHandler(IManualTripExtensionRepository extensions) => _extensions = extensions;

    public async Task<Result> Handle(AcknowledgeTripCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension — not assigned to an operator.");
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure("Trip is assigned to a different operator.");

        ext.MarkAcknowledged();
        await _extensions.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
