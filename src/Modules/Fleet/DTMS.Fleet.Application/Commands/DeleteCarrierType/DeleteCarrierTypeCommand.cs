using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.DeleteCarrierType;

public enum DeleteCarrierTypeOutcome
{
    Deleted,
    NotFound,
    /// <summary>Refused by the guard — carriers still hold this type.</summary>
    Blocked
}

/// <summary>
/// Carries the outcome rather than folding a refusal into <c>Result.Failure</c>,
/// for the same reason as <see cref="Commands.DeleteCarrier.DeleteCarrierResult"/>:
/// the three cases need three status codes, and the endpoint is where that
/// mapping belongs.
/// </summary>
public record DeleteCarrierTypeResult(DeleteCarrierTypeOutcome Outcome, string? Reason = null);

/// <summary>
/// Removes a carrier type from the catalogue. Unlike a carrier there is no
/// retirement here — a type is a catalogue entry, not an asset with a history —
/// so the only guard is that nothing still refers to it.
/// </summary>
public record DeleteCarrierTypeCommand(string Code) : ICommand<DeleteCarrierTypeResult>;

internal sealed class DeleteCarrierTypeCommandHandler
    : ICommandHandler<DeleteCarrierTypeCommand, DeleteCarrierTypeResult>
{
    private readonly ICarrierTypeRepository _carrierTypes;
    private readonly ICarrierRepository _carriers;
    private readonly IAttachmentRepository _attachments;

    public DeleteCarrierTypeCommandHandler(
        ICarrierTypeRepository carrierTypes,
        ICarrierRepository carriers,
        IAttachmentRepository attachments)
    {
        _carrierTypes = carrierTypes;
        _carriers = carriers;
        _attachments = attachments;
    }

    public async Task<Result<DeleteCarrierTypeResult>> Handle(
        DeleteCarrierTypeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Result<DeleteCarrierTypeResult>.Failure("Code is required.");

        var carrierType = await _carrierTypes.GetByCodeAsync(request.Code, cancellationToken);
        if (carrierType is null)
            return Result<DeleteCarrierTypeResult>.Success(new DeleteCarrierTypeResult(
                DeleteCarrierTypeOutcome.NotFound,
                $"CarrierType '{request.Code.Trim().ToUpperInvariant()}' not found."));

        var carriersUsing = await _carriers.CountForCarrierTypeAsync(carrierType.Id, cancellationToken);

        var (canDelete, reason) = carrierType.CanDelete(carriersUsing);
        if (!canDelete)
            return Result<DeleteCarrierTypeResult>.Success(
                new DeleteCarrierTypeResult(DeleteCarrierTypeOutcome.Blocked, reason));

        // Photos are removed here, not left to the FK cascade, for exactly the
        // reason spelled out in DeleteCarrierCommandHandler: the cascade runs
        // below EF, so no domain event fires and the bytes would stay in the
        // bucket forever with nothing left that knows their keys.
        var attachments = await _attachments.ListForOwnerForDeleteAsync(
            AttachmentOwner.CarrierType, carrierType.Id, cancellationToken);
        foreach (var attachment in attachments)
        {
            attachment.MarkObjectsOrphaned();
            _attachments.Remove(attachment);
        }

        _carrierTypes.Remove(carrierType);

        // One save for all of it — the repositories share the scoped
        // FleetDbContext, so the row, its images, and the instructions to clear
        // their bytes commit together or not at all.
        await _carrierTypes.SaveChangesAsync(cancellationToken);

        return Result<DeleteCarrierTypeResult>.Success(
            new DeleteCarrierTypeResult(DeleteCarrierTypeOutcome.Deleted));
    }
}
