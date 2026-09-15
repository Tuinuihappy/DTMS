using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetAttachments;

/// <summary>Images for one owner, newest first.</summary>
public record GetAttachmentsQuery(AttachmentOwner Owner, Guid OwnerId)
    : IQuery<IReadOnlyList<AttachmentDto>>;

/// <summary>
/// What is known about each image — no addresses.
///
/// <para>Signed URLs used to come back with every row, two per image. They were
/// dropped once the client moved to the stable image routes
/// (GetAttachmentImageQuery): a signature embeds a timestamp, so each list call
/// minted URLs no browser could cache, and nothing read them any more. The id is
/// all a client needs to build an image's address.</para>
/// </summary>
public record AttachmentDto(
    Guid Id,
    string ContentType,
    long SizeBytes,
    string? OriginalFileName,
    string? Caption,
    DateTime UploadedAt,
    string UploadedBy);

internal sealed class GetAttachmentsQueryHandler
    : IQueryHandler<GetAttachmentsQuery, IReadOnlyList<AttachmentDto>>
{
    private readonly IAttachmentRepository _attachments;

    public GetAttachmentsQueryHandler(IAttachmentRepository attachments)
        => _attachments = attachments;

    public async Task<Result<IReadOnlyList<AttachmentDto>>> Handle(
        GetAttachmentsQuery request, CancellationToken cancellationToken)
    {
        var rows = await _attachments.ListForOwnerAsync(
            request.Owner, request.OwnerId, cancellationToken);

        return Result<IReadOnlyList<AttachmentDto>>.Success(rows
            .Select(a => new AttachmentDto(
                Id: a.Id,
                ContentType: a.ContentType,
                SizeBytes: a.SizeBytes,
                OriginalFileName: a.OriginalFileName,
                Caption: a.Caption,
                UploadedAt: a.UploadedAt,
                UploadedBy: a.UploadedBy))
            .ToList());
    }
}
