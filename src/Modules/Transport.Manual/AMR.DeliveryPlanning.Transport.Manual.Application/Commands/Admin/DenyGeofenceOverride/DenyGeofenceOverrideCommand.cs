using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.Admin.DenyGeofenceOverride;

// POST /api/admin/geofence-overrides/{id}/deny — supervisor declines.
// Reason is mandatory and shown to the operator (they need to know
// whether to resubmit or fall back to the supervisor by phone).
public record DenyGeofenceOverrideCommand(
    Guid OverrideRequestId,
    Guid DecidedByOperatorId,
    string Reason) : ICommand;

internal sealed class DenyGeofenceOverrideCommandHandler : ICommandHandler<DenyGeofenceOverrideCommand>
{
    private readonly IGeofenceOverrideRequestRepository _overrides;
    public DenyGeofenceOverrideCommandHandler(IGeofenceOverrideRequestRepository overrides)
        => _overrides = overrides;

    public async Task<Result> Handle(DenyGeofenceOverrideCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("Denial reason is required.");

        var record = await _overrides.GetByIdAsync(request.OverrideRequestId, cancellationToken);
        if (record is null)
            return Result.Failure($"Override request {request.OverrideRequestId} not found.");

        try
        {
            record.Deny(request.DecidedByOperatorId, request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
        _overrides.Update(record);
        await _overrides.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
