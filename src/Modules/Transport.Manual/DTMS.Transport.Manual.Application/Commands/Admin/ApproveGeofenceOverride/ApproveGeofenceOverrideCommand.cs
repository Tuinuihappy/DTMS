using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Commands.Admin.ApproveGeofenceOverride;

// POST /api/admin/geofence-overrides/{id}/approve — supervisor/admin
// allows the operator to record pickup/drop despite the geofence fail.
// DecidedByOperatorId is the supervisor's DTMS operator Id (resolved
// from their JWT claims via OperatorSyncMiddleware on admin requests
// too — supervisors are still Operator-table rows per ADR-014).
public record ApproveGeofenceOverrideCommand(
    Guid OverrideRequestId,
    Guid DecidedByOperatorId,
    string? Note) : ICommand;

internal sealed class ApproveGeofenceOverrideCommandHandler : ICommandHandler<ApproveGeofenceOverrideCommand>
{
    private readonly IGeofenceOverrideRequestRepository _overrides;
    public ApproveGeofenceOverrideCommandHandler(IGeofenceOverrideRequestRepository overrides)
        => _overrides = overrides;

    public async Task<Result> Handle(ApproveGeofenceOverrideCommand request, CancellationToken cancellationToken)
    {
        var record = await _overrides.GetByIdAsync(request.OverrideRequestId, cancellationToken);
        if (record is null)
            return Result.Failure($"Override request {request.OverrideRequestId} not found.");

        try
        {
            record.Approve(request.DecidedByOperatorId, request.Note);
        }
        catch (InvalidOperationException ex)
        {
            // Aggregate guards (already decided / expired) surface as
            // 400-ish errors to the caller — the dispatcher UI shows
            // the message verbatim.
            return Result.Failure(ex.Message);
        }
        _overrides.Update(record);
        await _overrides.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
