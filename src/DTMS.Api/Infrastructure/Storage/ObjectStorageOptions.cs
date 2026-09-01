namespace DTMS.Api.Infrastructure.Storage;

// Bound from configuration section "ObjectStorage" (see appsettings +
// docker-compose env). Two endpoints because MinIO inside the docker
// network is reachable as "minio:9000" but the browser the PWA runs
// in only sees the published port — we sign URLs against the public
// endpoint so the operator's PUT actually resolves.
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    // Server-side endpoint the .NET app uses to reach MinIO (talks to
    // the docker-network host or the internal LB in production).
    public string Endpoint { get; set; } = "minio:9000";

    // URL the presigned upload embeds — what the browser sees. Differs
    // from Endpoint when MinIO is behind a reverse proxy or when the
    // PWA runs on a different host than the .NET app.
    public string PublicEndpoint { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = false;

    // Property name deliberately unchanged from when this was POD-only:
    // ObjectStorage__PodBucket is already set in docker-compose, and
    // renaming it would silently fall back to the default on deploy.
    public string PodBucket { get; set; } = "dtms-pod";

    // Carrier / carrier-type / maintenance images (ADR-019).
    public string AttachmentBucket { get; set; } = "dtms-attachments";

    // Upload ceiling, enforced by the storage server through the upload
    // policy — not by us. Client-side compression targets ~600 KiB but
    // falls back to the untouched file when it cannot decode the input
    // (old HEIC, non-images), so a phone original can arrive unshrunk.
    // MinIO writes to a volume on a disk that has filled and hung the
    // docker daemon before; this is the ceiling that prevents a repeat.
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    // An exact-match allow-list, NOT a "image/" prefix. A prefix would
    // admit image/svg+xml, and SVG executes script when served under its
    // own content type — stored XSS on the storage origin.
    public string[] AllowedContentTypes { get; set; } =
        ["image/jpeg", "image/png", "image/webp"];

    // Per owner (one carrier, one maintenance episode…). Checked at
    // presign and again at confirm; concurrent confirms can still both
    // pass, which costs at most one extra image and is not worth a
    // constraint trigger to prevent.
    public int MaxAttachmentsPerOwner { get; set; } = 10;

    // Short: the window in which a leaked upload URL is usable.
    public TimeSpan UploadTtl { get; set; } = TimeSpan.FromMinutes(10);

    // Longer, because a presigned URL is regenerated with a fresh
    // timestamp on every call and the browser treats each one as a
    // different resource. A short TTL therefore does not buy much
    // safety, it just forces the client to re-request more often —
    // the client caches the URL for its lifetime instead.
    public TimeSpan DownloadTtl { get; set; } = TimeSpan.FromHours(1);

    // Staging prefix retention. Anything still sitting here after this
    // long was never confirmed, so it is garbage by definition — which
    // is what lets a MinIO lifecycle rule collect it instead of code
    // that would need the authority to delete real objects.
    public int IncomingRetentionDays { get; set; } = 1;
}
