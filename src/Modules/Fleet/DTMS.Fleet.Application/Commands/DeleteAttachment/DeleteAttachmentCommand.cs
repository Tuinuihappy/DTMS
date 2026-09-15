using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.DeleteAttachment;

/// <summary>
/// Removes one image.
///
/// <para>This is the path users take most often, and the one where leaking is
/// easiest to miss: dropping the row makes the image disappear from the screen,
/// which looks like success while the bytes stay in the bucket forever. The
/// aggregate therefore announces its objects as orphaned before it goes, and
/// the DbContext interceptor turns that into an outbox row inside the same
/// transaction — so the row disappearing and the instruction to clear its bytes
/// either both happen or neither does.</para>
///
/// <para><b>The owner is part of the request, and must match.</b> The endpoint is
/// registered once per owner kind and guarded by that owner's write permission.
/// An image that is not the named owner's is treated as missing — which is what
/// stops the carrier route, guarded by CarrierWrite, from deleting a carrier
/// type's picture for someone who may not edit carrier types.</para>
/// </summary>
public record DeleteAttachmentCommand(AttachmentOwner Owner, Guid OwnerId, Guid AttachmentId) : ICommand;

internal sealed class DeleteAttachmentCommandHandler : ICommandHandler<DeleteAttachmentCommand>
{
    private readonly IAttachmentRepository _attachments;

    public DeleteAttachmentCommandHandler(IAttachmentRepository attachments)
        => _attachments = attachments;

    public async Task<Result> Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        // Tracked, unlike the read paths: MarkObjectsOrphaned raises an event the
        // interceptor only drains from entries the change tracker holds.
        var attachment = await _attachments.GetByIdAsync(request.AttachmentId, cancellationToken);

        // Checked before anything is touched. Someone else's image and no image
        // get the same answer, so the response does not confirm that an id exists
        // under an owner the caller may not edit.
        if (attachment is null
            || attachment.Owner != request.Owner
            || attachment.OwnerId != request.OwnerId)
            return Result.Failure("Image not found.");

        // Before Remove: the interceptor reads events off entries the change
        // tracker still holds, and it needs the keys while the row has them.
        attachment.MarkObjectsOrphaned();
        _attachments.Remove(attachment);

        await _attachments.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
