using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.MoveCarrier;

/// <summary>
/// Records where the carrier was last seen. Free text by design — it is not
/// resolved against stations or WMS locations until P3 gives it system meaning.
/// Allowed in every status: a cart under repair or already retired still moves,
/// to the workshop or the scrap yard, and recording that is the point.
/// </summary>
public record MoveCarrierCommand(string CarrierCode, string? CurrentLocationCode) : ICommand;

internal sealed class MoveCarrierCommandHandler : ICommandHandler<MoveCarrierCommand>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICurrentActorContext _actor;

    public MoveCarrierCommandHandler(ICarrierRepository carriers, ICurrentActorContext actor)
    {
        _carriers = carriers;
        _actor = actor;
    }

    public async Task<Result> Handle(MoveCarrierCommand request, CancellationToken cancellationToken)
    {
        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result.Failure($"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        carrier.MoveTo(request.CurrentLocationCode, ActorName.Of(_actor));
        await _carriers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
