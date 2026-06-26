using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Queries.GetPodPresignedUrl;

// POST /api/operator/pod/presign — operator app calls this to get a
// presigned PUT URL it can upload the POD photo to. Kind is "pickup"
// or "drop" (other values rejected by the handler — see PodObjectKey).
// The returned ObjectKey is what the operator passes back on
// RecordPickup/RecordDrop so DTMS can stamp it onto the extension.
public record GetPodPresignedUrlQuery(
    Guid TripId,
    Guid OperatorId,
    string Kind,
    string? FileExtension = "jpg") : IQuery<PodPresignedUrlDto>;

public record PodPresignedUrlDto(
    string UploadUrl,
    string ObjectKey,
    DateTime ExpiresAt);
