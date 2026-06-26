namespace DTMS.Transport.Manual.Application.Services;

// Phase 4.3 — POD upload mechanism per ADR-015. Presigned PUT URLs
// keep photo bytes off the .NET server: operator PWA receives a URL +
// object key from /api/operator/pod/presign and uploads the photo
// directly to MinIO. The follow-up pickup/drop call references the key.
//
// Contract is intentionally narrow — Phase 4.3 needs PUT presign +
// HEAD validation. Add GET presign (for dispatcher console preview) in
// 4.6 when the UI needs to render uploaded photos.
public interface IObjectStorageService
{
    // Generate a time-limited URL the operator PWA can PUT the photo
    // bytes to. ObjectKey is the canonical reference DTMS stores on
    // ManualTripExtension.PickupPodKey / DropPodKey — keep it stable
    // across the presign call and the subsequent record-pickup call.
    Task<PresignedUploadUrl> GeneratePresignedPutAsync(
        string bucket,
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default);

    // After the PWA reports a successful upload, the API verifies the
    // object actually landed before stamping the trip. Returns false
    // if the object is absent (operator may have failed mid-upload).
    Task<bool> ObjectExistsAsync(string bucket, string objectKey, CancellationToken ct = default);

    // Idempotent — called on startup to make sure the POD bucket
    // exists. Safe to call repeatedly; no-op if already present.
    Task EnsureBucketExistsAsync(string bucket, CancellationToken ct = default);
}

public record PresignedUploadUrl(
    string UploadUrl,
    string ObjectKey,
    DateTime ExpiresAt);
