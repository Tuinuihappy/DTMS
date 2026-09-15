using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;

namespace DTMS.Fleet.Application.Queries.GetAttachmentImage;

public enum AttachmentImageSize
{
    /// <summary>The small copy made at upload, for tables and grids.</summary>
    Thumbnail,

    /// <summary>The stored image itself, for viewing one photo.</summary>
    Full,
}

/// <summary>
/// A short-lived signed URL for one image, for the server-side relay that turns
/// it into a stable, cacheable address.
///
/// <para>The owner is part of the request, not looked up from the image. The
/// endpoint that sends this is registered once per owner kind and guarded by that
/// owner's read permission; requiring the image to belong to the named owner is
/// what stops the carrier route — guarded by CarrierRead — from serving a carrier
/// type's picture to someone who may not read carrier types.</para>
/// </summary>
public record GetAttachmentImageQuery(
    AttachmentOwner Owner, Guid OwnerId, Guid AttachmentId, AttachmentImageSize Size)
    : IQuery<string>;

internal sealed class GetAttachmentImageQueryHandler
    : IQueryHandler<GetAttachmentImageQuery, string>
{
    // Minutes, not the hour GetAttachmentsQuery uses. This URL never reaches a
    // browser: the frontend relay fetches it the instant it is issued and serves
    // the bytes under an address of its own. Nothing is gained by leaving a
    // capability valid for longer than that one request needs.
    public static readonly TimeSpan UrlTtl = TimeSpan.FromMinutes(5);

    private readonly IAttachmentRepository _attachments;
    private readonly IObjectStorageService _storage;

    public GetAttachmentImageQueryHandler(
        IAttachmentRepository attachments,
        IObjectStorageService storage)
    {
        _attachments = attachments;
        _storage = storage;
    }

    public async Task<Result<string>> Handle(
        GetAttachmentImageQuery request, CancellationToken cancellationToken)
    {
        var attachment = await _attachments.FindForOwnerAsync(
            request.Owner, request.OwnerId, request.AttachmentId, cancellationToken);

        // Missing and "belongs to someone else" are the same answer, and neither
        // signs anything.
        if (attachment is null)
            return Result<string>.Failure("Image not found.");

        // The full image stands in when no thumbnail could be made at upload —
        // heavier, but a picture beats an empty cell.
        var key = request.Size == AttachmentImageSize.Full
            ? attachment.ObjectKey
            : attachment.ThumbnailKey ?? attachment.ObjectKey;

        var url = await _storage.GeneratePresignedGetAsync(
            attachment.Bucket,
            key,
            UrlTtl,
            // Served as the type recorded on the row, as GetAttachmentsQuery does,
            // so a stray object with a scriptable type could never be served as one.
            attachment.ContentType,
            cancellationToken);

        return Result<string>.Success(url);
    }
}
