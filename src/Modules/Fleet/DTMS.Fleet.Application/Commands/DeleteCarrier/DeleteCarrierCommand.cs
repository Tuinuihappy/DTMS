using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.DeleteCarrier;

public enum DeleteCarrierOutcome
{
    Deleted,
    NotFound,
    /// <summary>Refused by the guard — the carrier has history worth keeping.</summary>
    Blocked
}

/// <summary>
/// Carries the outcome rather than folding a refusal into <c>Result.Failure</c>,
/// because the three cases need three different status codes and the endpoint is
/// where that mapping belongs. Squeezing them through one failure string would
/// force the HTTP layer to sniff the message.
/// </summary>
public record DeleteCarrierResult(DeleteCarrierOutcome Outcome, string? Reason = null);

/// <summary>
/// Destroys a carrier that should never have existed — a typo, a double entry.
/// This is not retirement: it removes the row and frees the code.
///
/// <para>Permitted only when the carrier has no history at all, which is what
/// keeps it from becoming a back door around codes being unique forever
/// (ADR-019): anything with history cannot be deleted, so no historical
/// statement about a code can be made ambiguous by that code being freed. P3
/// adds trip assignments to the same guard.</para>
/// </summary>
public record DeleteCarrierCommand(string CarrierCode) : ICommand<DeleteCarrierResult>;

internal sealed class DeleteCarrierCommandHandler : ICommandHandler<DeleteCarrierCommand, DeleteCarrierResult>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierMaintenanceLogRepository _logs;
    private readonly IAttachmentRepository _attachments;

    public DeleteCarrierCommandHandler(
        ICarrierRepository carriers,
        ICarrierMaintenanceLogRepository logs,
        IAttachmentRepository attachments)
    {
        _carriers = carriers;
        _logs = logs;
        _attachments = attachments;
    }

    public async Task<Result<DeleteCarrierResult>> Handle(
        DeleteCarrierCommand request, CancellationToken cancellationToken)
    {
        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result<DeleteCarrierResult>.Success(new DeleteCarrierResult(
                DeleteCarrierOutcome.NotFound,
                $"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found."));

        var maintenanceCount = await _logs.CountForCarrierAsync(carrier.Id, cancellationToken);

        var (canDelete, reason) = carrier.CanDelete(maintenanceCount);
        if (!canDelete)
            return Result<DeleteCarrierResult>.Success(
                new DeleteCarrierResult(DeleteCarrierOutcome.Blocked, reason));

        // The FK to CarrierMaintenanceLog cascades, but the guard above means
        // there is never anything for it to cascade to — history is exactly what
        // makes a carrier undeletable. That reasoning does NOT extend to images:
        // a photo is not history in the sense CanDelete protects, so it never
        // blocks a delete, so a deletable carrier can very much have some.
        //
        // Left to the database cascade they would vanish from the table while
        // their bytes stayed in the bucket forever — the cascade runs below EF,
        // so no aggregate is loaded, no domain event fires, and nothing
        // downstream ever hears about it. Removing them here instead means each
        // one announces its own objects while it still knows what they are.
        var attachments = await _attachments.ListForCarrierForDeleteAsync(carrier.Id, cancellationToken);
        foreach (var attachment in attachments)
        {
            attachment.MarkObjectsOrphaned();
            _attachments.Remove(attachment);
        }

        _carriers.Remove(carrier);

        // One save for all of it. The repositories share the scoped
        // FleetDbContext, so the carrier, its images, and the instructions to
        // clear their bytes commit together or not at all.
        await _carriers.SaveChangesAsync(cancellationToken);

        return Result<DeleteCarrierResult>.Success(new DeleteCarrierResult(DeleteCarrierOutcome.Deleted));
    }
}
