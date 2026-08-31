using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.RetireCarrier;

/// <summary>
/// End of life. The row, its history, and its code reservation all stay —
/// reversible via un-retire, so a mis-click is not a permanent dead row.
/// For a carrier that should never have existed, delete is the right operation.
/// </summary>
public record RetireCarrierCommand(string CarrierCode, string Reason) : ICommand;

internal sealed class RetireCarrierCommandHandler : ICommandHandler<RetireCarrierCommand>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierMaintenanceLogRepository _logs;
    private readonly ICurrentActorContext _actor;

    public RetireCarrierCommandHandler(
        ICarrierRepository carriers,
        ICarrierMaintenanceLogRepository logs,
        ICurrentActorContext actor)
    {
        _carriers = carriers;
        _logs = logs;
        _actor = actor;
    }

    public async Task<Result> Handle(RetireCarrierCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("Reason is required.");

        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result.Failure($"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        var actor = ActorName.Of(_actor);

        // Read the open episode before retiring: the aggregate clears the
        // carrier's maintenance fields, and an episode left open would block the
        // partial unique index forever if the carrier were ever un-retired and
        // sent for repair again.
        var open = await _logs.GetOpenForCarrierAsync(carrier.Id, cancellationToken);

        try
        {
            carrier.Retire(request.Reason, actor);
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message); }

        open?.End("Retired while under maintenance", actor);

        await _carriers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
