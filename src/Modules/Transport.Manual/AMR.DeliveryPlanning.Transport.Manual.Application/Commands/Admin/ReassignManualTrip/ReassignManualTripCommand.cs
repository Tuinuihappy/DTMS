using DTMS.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.Admin.ReassignManualTrip;

// POST /api/admin/manual-trips/{tripId}/reassign — dispatcher moves an
// active Manual trip from its current operator to a different one.
// Useful when an operator goes off-shift unexpectedly, can't reach the
// pickup, or is being load-balanced manually.
//
// Refuses the swap if:
//   - The new operator is not Active
//   - The new operator already has a different active trip (single-
//     active-trip invariant per Operator aggregate)
//   - The trip has already been dropped (post-drop is operator-agnostic;
//     the trip is essentially closed at that point)
public record ReassignManualTripCommand(
    Guid TripId,
    Guid NewOperatorId,
    string? Reason) : ICommand;

internal sealed class ReassignManualTripCommandHandler : ICommandHandler<ReassignManualTripCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly IOperatorRepository _operators;
    private readonly IPushNotificationGateway _push;
    private readonly ManualDispatchOptions _options;
    private readonly ILogger<ReassignManualTripCommandHandler> _logger;

    public ReassignManualTripCommandHandler(
        IManualTripExtensionRepository extensions,
        IOperatorRepository operators,
        IPushNotificationGateway push,
        IOptions<ManualDispatchOptions> options,
        ILogger<ReassignManualTripCommandHandler> logger)
    {
        _extensions = extensions;
        _operators = operators;
        _push = push;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result> Handle(ReassignManualTripCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension.");
        if (ext.OperatorId == request.NewOperatorId)
            return Result.Failure("Trip is already assigned to that operator.");

        var newOp = await _operators.GetByIdAsync(request.NewOperatorId, cancellationToken);
        if (newOp is null)
            return Result.Failure($"Operator {request.NewOperatorId} not found.");
        if (newOp.Status != OperatorStatus.Active)
            return Result.Failure($"Target operator is {newOp.Status} — only Active operators can take reassignments.");
        if (newOp.CurrentTripId.HasValue && newOp.CurrentTripId.Value != request.TripId)
            return Result.Failure(
                $"Target operator already has trip {newOp.CurrentTripId.Value} — complete or reassign that first.");

        var oldOp = await _operators.GetByIdAsync(ext.OperatorId, cancellationToken);
        // Old operator may have been deactivated — that's fine, we still
        // need to clear their CurrentTripId so they don't appear bound
        // to a trip they no longer carry. ClearTripAssignment is a no-op
        // when CurrentTripId is already null.
        oldOp?.ClearTripAssignment();

        var now = DateTime.UtcNow;
        var newAckDeadline = now.AddMinutes(_options.AckSlaMinutes);
        var newPickupDeadline = now.AddMinutes(_options.AckSlaMinutes + _options.PickupSlaMinutes);
        try
        {
            ext.ReassignToOperator(request.NewOperatorId, newAckDeadline, newPickupDeadline);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
        _extensions.Update(ext);

        try
        {
            newOp.AssignToTrip(request.TripId);
        }
        catch (InvalidOperationException ex)
        {
            // Shouldn't happen — guards above cover the cases — but if
            // it does, rolling back the ext change is the EF default
            // since we haven't committed yet.
            return Result.Failure(ex.Message);
        }

        await _operators.SaveChangesAsync(cancellationToken);
        await _extensions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ManualReassign] Trip {TripId} reassigned from {OldOperatorId} to {NewOperatorId} (reason: {Reason})",
            request.TripId, ext.OperatorId, request.NewOperatorId, request.Reason ?? "(none)");

        // Best-effort push — new operator gets immediate "you have a
        // new trip" ping. Old-operator notification is intentionally
        // skipped (their PWA's /trips/assigned poll will drop the row
        // on next refresh; explicit "you lost a trip" push would be
        // alarming for a routine reassignment).
        try
        {
            var shortTrip = request.TripId.ToString()[..8];
            await _push.SendToOperatorAsync(request.NewOperatorId, new PushNotificationPayload(
                Title: string.Format(_options.PushTitleTemplate, shortTrip),
                Body: "You've been reassigned a delivery. Tap to view.",
                Url: _options.PushTargetUrl,
                Tag: $"trip-{request.TripId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ManualReassign] Push to new operator {OperatorId} failed; reassignment succeeded regardless.",
                request.NewOperatorId);
        }

        return Result.Success();
    }
}
