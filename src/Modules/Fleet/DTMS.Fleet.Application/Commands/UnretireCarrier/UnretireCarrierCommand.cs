using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.UnretireCarrier;

/// <summary>
/// Undoes a retirement. Without it a mis-clicked retire is unrecoverable: the
/// carrier cannot return to service (that path starts from Maintenance) and
/// cannot be deleted (that path requires Available), so it would sit as a dead
/// row holding its code forever. Follows the same idiom as ReopenDeliveryOrder.
///
/// <para>Returns to Available rather than to whatever it was before. If the cart
/// still needs repair, send it to maintenance again — that records a truthful new
/// episode instead of reopening one that was already closed out.</para>
/// </summary>
public record UnretireCarrierCommand(string CarrierCode) : ICommand;

internal sealed class UnretireCarrierCommandHandler : ICommandHandler<UnretireCarrierCommand>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICurrentActorContext _actor;

    public UnretireCarrierCommandHandler(ICarrierRepository carriers, ICurrentActorContext actor)
    {
        _carriers = carriers;
        _actor = actor;
    }

    public async Task<Result> Handle(UnretireCarrierCommand request, CancellationToken cancellationToken)
    {
        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result.Failure($"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        try
        {
            carrier.Unretire(ActorName.Of(_actor));
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        await _carriers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
