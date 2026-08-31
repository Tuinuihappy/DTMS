using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.SetCarrierMaintenance;

/// <summary>Takes a carrier out of service and opens a maintenance episode.</summary>
public record SetCarrierMaintenanceCommand(string CarrierCode, string Reason) : ICommand;

internal sealed class SetCarrierMaintenanceCommandHandler : ICommandHandler<SetCarrierMaintenanceCommand>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierMaintenanceLogRepository _logs;
    private readonly ICurrentActorContext _actor;

    public SetCarrierMaintenanceCommandHandler(
        ICarrierRepository carriers,
        ICarrierMaintenanceLogRepository logs,
        ICurrentActorContext actor)
    {
        _carriers = carriers;
        _logs = logs;
        _actor = actor;
    }

    public async Task<Result> Handle(SetCarrierMaintenanceCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("Reason is required.");

        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result.Failure($"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        var actor = ActorName.Of(_actor);

        try
        {
            carrier.EnterMaintenance(request.Reason, actor);
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message); }

        await _logs.AddAsync(new CarrierMaintenanceLog(carrier.Id, request.Reason, actor), cancellationToken);

        // One save for both. The repositories share the scoped FleetDbContext,
        // so the status change and the log row commit together — saving through
        // each would split one state change across two transactions and could
        // leave a carrier marked under maintenance with no episode recorded.
        await _carriers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
