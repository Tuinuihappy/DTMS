using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Queries.GetPodPresignedUrl;

// POST /api/operator/pod/presign — operator app calls this to get a signed
// form post it can upload the POD photo to. Kind is "pickup" or "drop"
// (other values rejected by the handler — see PodObjectKey). The returned
// ObjectKey is what the operator passes back on RecordPickup/RecordDrop so
// DTMS can stamp it onto the extension.
public record GetPodPresignedUrlQuery(
    Guid TripId,
    Guid OperatorId,
    string Kind,
    string? FileExtension = "jpg") : IQuery<PodPresignedUrlDto>;

/// <summary>
/// A signed form post rather than a plain upload URL.
///
/// <para><paramref name="Fields"/> must be written into the multipart body
/// <b>before</b> the file part — S3-compatible servers read the policy from
/// the leading fields and reject a body that puts the file first.</para>
/// </summary>
public record PodPresignedUrlDto(
    string Url,
    IReadOnlyDictionary<string, string> Fields,
    string ObjectKey,
    DateTime ExpiresAt);
