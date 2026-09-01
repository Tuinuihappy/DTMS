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
/// </summary>
public record DeleteAttachmentCommand(Guid AttachmentId) : ICommand;

internal sealed class DeleteAttachmentCommandHandler : ICommandHandler<DeleteAttachmentCommand>
{
    private readonly IAttachmentRepository _attachments;

    public DeleteAttachmentCommandHandler(IAttachmentRepository attachments)
        => _attachments = attachments;

    public async Task<Result> Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        var attachment = await _attachments.GetByIdAsync(request.AttachmentId, cancellationToken);
        if (attachment is null)
            return Result.Failure("Image not found.");

        // Before Remove: the interceptor reads events off entries the change
        // tracker still holds, and it needs the keys while the row has them.
        attachment.MarkObjectsOrphaned();
        _attachments.Remove(attachment);

        await _attachments.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
