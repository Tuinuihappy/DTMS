using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;

namespace DTMS.Fleet.Application.Queries.GetAttachments;

/// <summary>Images for one owner, newest first.</summary>
public record GetAttachmentsQuery(AttachmentOwner Owner, Guid OwnerId)
    : IQuery<IReadOnlyList<AttachmentDto>>;

/// <summary>
/// The URLs come back with the row rather than from a per-image endpoint.
///
/// <para>Signing is a local computation — it does not call storage — so signing
/// ten costs what signing one costs, and a separate lookup per image would turn
/// a ten-image gallery into eleven round trips for nothing.</para>
///
/// <para>Both URLs expire. The signature embeds a timestamp, so every call
/// produces a different URL and a browser treats each as a new resource; the
/// client should hold these for their lifetime rather than re-requesting on
/// every render.</para>
/// </summary>
public record AttachmentDto(
    Guid Id,
    string Url,
    string? ThumbnailUrl,
    string ContentType,
    long SizeBytes,
    string? OriginalFileName,
    string? Caption,
    DateTime UploadedAt,
    string UploadedBy,
    DateTime ExpiresAt);

internal sealed class GetAttachmentsQueryHandler
    : IQueryHandler<GetAttachmentsQuery, IReadOnlyList<AttachmentDto>>
{
    // Deliberately not minutes. A shorter window would not make the images
    // meaningfully safer — it only forces more re-signing, and each new URL is
    // another chance for one to end up somewhere it should not. It is long
    // enough that a client can cache a URL for a working session.
    public static readonly TimeSpan UrlTtl = TimeSpan.FromHours(1);

    private readonly IAttachmentRepository _attachments;
    private readonly IObjectStorageService _storage;

    public GetAttachmentsQueryHandler(
        IAttachmentRepository attachments,
        IObjectStorageService storage)
    {
        _attachments = attachments;
        _storage = storage;
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> Handle(
        GetAttachmentsQuery request, CancellationToken cancellationToken)
    {
        var rows = await _attachments.ListForOwnerAsync(
            request.Owner, request.OwnerId, cancellationToken);

        var expiresAt = DateTime.UtcNow.Add(UrlTtl);
        var dtos = new List<AttachmentDto>(rows.Count);

        foreach (var a in rows)
        {
            dtos.Add(new AttachmentDto(
                Id: a.Id,
                Url: await SignAsync(a.Bucket, a.ObjectKey, a.ContentType, cancellationToken),
                ThumbnailUrl: a.ThumbnailKey is null
                    ? null
                    : await SignAsync(a.Bucket, a.ThumbnailKey, a.ContentType, cancellationToken),
                ContentType: a.ContentType,
                SizeBytes: a.SizeBytes,
                OriginalFileName: a.OriginalFileName,
                Caption: a.Caption,
                UploadedAt: a.UploadedAt,
                UploadedBy: a.UploadedBy,
                ExpiresAt: expiresAt));
        }

        return Result<IReadOnlyList<AttachmentDto>>.Success(dtos);
    }

    private Task<string> SignAsync(string bucket, string key, string contentType, CancellationToken ct)
        // The served type is forced to the one recorded on the row rather than
        // left to whatever the object carries. Objects written through the
        // upload policy already match, but stating it means a stray object with
        // a scriptable type could never be served as one.
        => _storage.GeneratePresignedGetAsync(bucket, key, UrlTtl, contentType, ct);
}
