namespace AMR.DeliveryPlanning.Transport.Manual.Infrastructure.Storage;

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

    // URL the presigned PUT embeds — what the browser sees. Differs
    // from Endpoint when MinIO is behind a reverse proxy or when the
    // PWA runs on a different host than the .NET app.
    public string PublicEndpoint { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = false;

    // Default bucket for POD photos. Auto-created on startup.
    public string PodBucket { get; set; } = "dtms-pod";
}
