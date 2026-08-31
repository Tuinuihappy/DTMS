using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Fleet.Application.Commands.ReturnCarrierToService;

/// <summary>Brings a carrier back and closes its maintenance episode.</summary>
public record ReturnCarrierToServiceCommand(string CarrierCode, string? Outcome = null) : ICommand;

internal sealed class ReturnCarrierToServiceCommandHandler : ICommandHandler<ReturnCarrierToServiceCommand>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierMaintenanceLogRepository _logs;
    private readonly ICurrentActorContext _actor;
    private readonly ILogger<ReturnCarrierToServiceCommandHandler> _logger;

    public ReturnCarrierToServiceCommandHandler(
        ICarrierRepository carriers,
        ICarrierMaintenanceLogRepository logs,
        ICurrentActorContext actor,
        ILogger<ReturnCarrierToServiceCommandHandler> logger)
    {
        _carriers = carriers;
        _logs = logs;
        _actor = actor;
        _logger = logger;
    }

    public async Task<Result> Handle(ReturnCarrierToServiceCommand request, CancellationToken cancellationToken)
    {
        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result.Failure($"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        var actor = ActorName.Of(_actor);

        try
        {
            carrier.ReturnToService(actor);
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        var open = await _logs.GetOpenForCarrierAsync(carrier.Id, cancellationToken);
        if (open is not null)
        {
            open.End(request.Outcome, actor);
        }
        else
        {
            // Data from before the log existed, or a row closed out of band.
            // The carrier's real status is what operations depend on, so bring
            // it back and record the gap rather than blocking the workshop on an
            // incomplete history.
            _logger.LogWarning(
                "Carrier {CarrierCode} returned to service with no open maintenance episode — " +
                "status updated, history has a gap", carrier.CarrierCode);
        }

        await _carriers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
