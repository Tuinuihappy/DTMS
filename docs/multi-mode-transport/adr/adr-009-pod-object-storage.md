# ADR-009: Proof of Delivery (POD) Object Storage

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [Phase 4](../phases/phase-4-transport-manual.md), [ADR-005](adr-005-push-notification-gateway.md), [ADR-007](adr-007-mobile-api-authentication.md)

## Context

Phase 4 (Manual mode) ต้องเก็บ POD evidence จาก operator mobile app:
- **Pickup photo** — proof ของรับสินค้า (per-trip, optional แต่ปกติบังคับ)
- **Drop photo** — proof ของส่งสินค้า
- **Drop signature** — ลายเซ็นผู้รับ (PNG transparent background)
- **Exception photos** — กรณีมีปัญหา (cargo damaged, address wrong) — 0-N per exception

Existing infrastructure:
- [`ProofOfDelivery.cs`](../../../src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Entities/ProofOfDelivery.cs) entity มีอยู่ — เก็บ `PhotoUrl`, `SignatureData`, `ScannedIds`
- Schema มี URL columns (strings) อยู่แล้ว — file storage ตัดสินยังไงไม่ได้ระบุ
- ไม่มี object storage abstraction ใน codebase ปัจจุบัน
- Local dev = Docker compose stack (Postgres + RabbitMQ + Redis); no MinIO yet

Requirements:
1. **High volume**: 1000 operators × 5 trips/day × 3 photos/trip = 15k photos/day = ~5GB/day
2. **Long retention**: 1 year minimum (compliance — proof of delivery disputes)
3. **Fast upload**: mobile app on 4G — large photos slow → must compress + chunk
4. **Secure access**: ห้ามให้คนนอก operator + dispatcher download
5. **Cost-effective at scale**: $0.02/GB/month range (not Snowflake-tier)
6. **Local dev parity**: developer ไม่ต้อง AWS account
7. **Multi-region future**: Thailand primary, regional DR backup
8. **GDPR-adjacent**: customer signatures = personal data; need delete capability

## Decision

ใช้ **S3-compatible object storage** with **server-mediated uploads** (NOT presigned URLs) behind `IObjectStorageService` abstraction:

### Storage Backend per Environment

| Environment | Backend | Bucket | Endpoint |
|---|---|---|---|
| **Local dev** | MinIO (Docker) | `dtms-pod-dev` | http://localhost:9000 |
| **CI** | MinIO (Testcontainers) | `dtms-pod-test` | dynamic |
| **Staging** | S3-compatible (Wasabi/Backblaze B2) | `dtms-pod-staging` | regional |
| **Production** | AWS S3 (ap-southeast-1) | `dtms-pod` | https://s3.ap-southeast-1.amazonaws.com |

→ S3 protocol = portable across providers; ลด vendor lock-in

### Abstraction

```csharp
public interface IObjectStorageService
{
    Task<ObjectKey> UploadAsync(UploadRequest request, CancellationToken ct);
    Task<Stream> DownloadAsync(ObjectKey key, CancellationToken ct);
    Task<Uri> GetTemporaryUrlAsync(ObjectKey key, TimeSpan validFor, CancellationToken ct);
    Task DeleteAsync(ObjectKey key, CancellationToken ct);
    Task<ObjectMetadata?> GetMetadataAsync(ObjectKey key, CancellationToken ct);
}

public sealed record UploadRequest(
    Stream Content,
    string ContentType,        // "image/jpeg", "image/png"
    long ContentLength,
    string ObjectPath,         // canonical key
    IReadOnlyDictionary<string, string>? Tags = null);

public sealed record ObjectKey(string Path);   // type-safe wrapper

public sealed record ObjectMetadata(
    string ContentType,
    long ContentLength,
    DateTime LastModified,
    string ETag,
    IReadOnlyDictionary<string, string> Tags);
```

### Upload Flow (Server-Mediated)

```
1. Mobile app: POST /api/operator/uploads/photo
   Body: multipart/form-data (image binary + metadata)
   Auth: JWT Bearer

2. Server validates:
   - JWT valid + operator authenticated
   - File size < 5MB
   - ContentType in [image/jpeg, image/png]
   - Magic bytes match content type (anti-spoofing)
   - Image dimensions ≤ 4096×4096

3. Server processes:
   - Re-encode to JPEG quality 85 (if photo) → ~300KB typical
   - Strip EXIF except GPS + timestamp
   - Generate object path: pod/{tripId}/{type}/{guid}.jpg
   - Compute SHA-256 of normalized content

4. Server uploads to object storage:
   - PutObject with Tags: { trip_id, operator_id, content_sha256 }

5. Server returns:
   { url: "https://.../pod/{tripId}/.../{guid}.jpg", contentSha256: "..." }

6. Mobile app uses returned URL in subsequent /pickup or /drop call
```

### Object Path Convention

```
pod/{tripId}/pickup-photo/{guid}.jpg
pod/{tripId}/drop-photo/{guid}.jpg
pod/{tripId}/signature/{guid}.png
pod/{tripId}/exception/{exceptionId}/{guid}.jpg
```

Path encodes: type (for retention policy), tripId (for cleanup on trip delete), guid (unguessable)

### Access Control

**ไม่ใช้ public ACL** — bucket private, ทุก access ผ่าน server:

```
GET /api/dispatcher/trips/{id}/pod/{type}
  → DTMS validates dispatcher has access to this trip's data
  → DTMS gets temporary signed URL (5 min validity)
  → Returns redirect (302) to signed URL
  → Browser fetches direct from S3
```

ใช้ temporary signed URLs (S3 presigned GET) ส่งให้ client — secure + offload bandwidth จาก DTMS

### Retention Policy

- **Active POD photos**: retained indefinitely while linked Trip exists
- **Trip soft-delete**: photos retained 1 year after Trip.DeletedAt
- **Trip hard-delete**: photos deleted immediately (cascade)
- **Exception photos**: same as trip
- **Lifecycle policy** (S3-side): tag-based auto-archive to S3 Glacier after 90 days inactive

```json
{
  "Rules": [{
    "Id": "ArchiveOldPod",
    "Status": "Enabled",
    "Filter": { "Prefix": "pod/" },
    "Transitions": [
      { "Days": 90,  "StorageClass": "STANDARD_IA" },
      { "Days": 365, "StorageClass": "GLACIER" }
    ]
  }]
}
```

### Why Server-Mediated (NOT Presigned PUT)

Decision rationale ที่ลึกที่สุดของ ADR นี้:

| Aspect | Server-Mediated (CHOSEN) | Presigned PUT URLs |
|---|---|---|
| Validation | Server validates content before upload | Trust client (or post-validate) |
| Re-encoding | Server normalizes (EXIF strip, compress) | Client decides quality |
| Bandwidth | DTMS handles upload bandwidth | Direct client → S3 (faster) |
| Implementation | Standard multipart endpoint | Generate URL → client uploads → server confirms |
| Security | Single auth check at server | Multi-step (URL leak risks) |
| Mobile complexity | Single API call | 2-step flow with retry |
| Best for | < 5MB files, validation-critical | Large files (videos), high throughput |

→ POD photos ≤ 500KB → server-mediated overhead negligible (~200ms extra) + much simpler

## Provider Choice Rationale

### Production: AWS S3 (ap-southeast-1 Singapore)

**Pros:**
- Mature, reliable
- Singapore region acceptable for Thailand data (low latency)
- Lifecycle policies + cross-region replication built-in
- S3 IAM granular permissions
- Cost predictable at our scale: 5GB/day × 365 = ~1.8TB/year = $40/month STANDARD + < $5 for IA tier

**Cons:**
- ราคาแพงกว่า B2/Wasabi (3-4x)
- Vendor lock-in (mitigated by S3 protocol)

### Alternative considered: Backblaze B2

- $0.005/GB/month (S3 = $0.023)
- S3-compatible API
- Free egress to Cloudflare CDN

→ Likely future migration target; start with AWS S3 for proven reliability

## Alternatives Considered

### Alternative A: PostgreSQL BLOB Storage (bytea column)

ใส่ photo binary ลง Trip table หรือ separate BLOB table

**Pros:**
- Transactional with Trip writes
- Backup includes photos automatically
- No external service

**Cons:**
- Postgres slow with BLOBs (max 1GB per row, recommended < 1MB)
- 5GB/day in DB = 1.8TB/year of bloat
- Bad query performance (table scans miss bytea pages)
- Backup size explodes
- Can't serve to browser directly

**Rejected because:** unfit for purpose at scale

### Alternative B: Local Filesystem (mounted volume)

```
/var/dtms/pod/{tripId}/...
```

**Pros:**
- Simple
- Free
- Fast access

**Cons:**
- Single-server (no horizontal scale)
- Backup = manual rsync or filesystem snapshots
- No CDN integration
- No lifecycle automation
- Container ephemerality concerns

**Rejected for production:** ใช้ได้ใน dev เฉพาะ; MinIO ให้ S3 compatibility ที่ดีกว่า

### Alternative C: Database File Storage (Postgres + pg_largeobject)

Use Postgres large objects API

**Pros:**
- Same backup as data
- Transactional

**Cons:**
- Same scale issues as bytea
- Library support uneven
- ORM (EF) doesn't model gracefully

**Rejected:** Same fundamental issue as Alternative A

### Alternative D: Azure Blob Storage

**Pros:**
- ถ้าโปรเจ็คใช้ Azure อยู่
- Geo-redundant by default

**Cons:**
- Team experience ต่ำกว่า AWS
- Thailand region only via Southeast Asia (Singapore — same as AWS ap-southeast-1)
- No clear advantage over S3 for our needs

**Rejected for now:** S3 ecosystem + tooling stronger

### Alternative E: Presigned PUT URLs (Direct Client → S3)

Mobile app gets temporary upload URL, uploads directly

**Pros:**
- Faster upload (no server hop)
- Server bandwidth saved

**Cons:**
- Skip server-side validation (or post-validate which is complex)
- Can't re-encode/compress server-side
- Mobile app must implement S3 SDK (or use signed URL with multipart)
- URL leakage = upload abuse

**Rejected because:** POD photos small enough that bandwidth saved is marginal; security + validation gain > bandwidth loss

### Alternative F: IPFS / decentralized storage

**Pros:** Trendy

**Cons:**
- Latency unpredictable
- Operations team ไม่มี experience
- Pinning = still need centralized service

**Rejected:** premature for current scale

## Implementation Details

### Service Registration

```csharp
// in Transport.Manual.Infrastructure
public static IServiceCollection AddObjectStorage(this IServiceCollection services, IConfiguration config)
{
    services.Configure<ObjectStorageOptions>(config.GetSection("ObjectStorage"));

    services.AddSingleton<IAmazonS3>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
        var s3Config = new AmazonS3Config
        {
            ServiceURL = opts.Endpoint,           // null for AWS default; set for MinIO
            ForcePathStyle = opts.ForcePathStyle, // true for MinIO
            UseHttp = opts.UseHttp                // true for local MinIO
        };
        return new AmazonS3Client(opts.AccessKey, opts.SecretKey, s3Config);
    });

    services.AddScoped<IObjectStorageService, S3ObjectStorageService>();
    return services;
}
```

### Config

```json
{
  "ObjectStorage": {
    "Provider": "S3",
    "BucketName": "dtms-pod",
    "Endpoint": null,                    // AWS default
    "Region": "ap-southeast-1",
    "AccessKey": "${AWS_ACCESS_KEY}",
    "SecretKey": "${AWS_SECRET_KEY}",
    "ForcePathStyle": false,
    "UseHttp": false,
    "MaxUploadSizeMB": 5,
    "AllowedContentTypes": ["image/jpeg", "image/png"],
    "TemporaryUrlValiditySeconds": 300
  }
}
```

Local dev:
```json
{
  "ObjectStorage": {
    "Provider": "S3",
    "BucketName": "dtms-pod-dev",
    "Endpoint": "http://localhost:9000",
    "Region": "us-east-1",               // MinIO accepts any
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "ForcePathStyle": true,
    "UseHttp": true
  }
}
```

### S3 Implementation

```csharp
public sealed class S3ObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly ObjectStorageOptions _options;

    public async Task<ObjectKey> UploadAsync(UploadRequest request, CancellationToken ct)
    {
        ValidateRequest(request);

        var put = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = request.ObjectPath,
            InputStream = request.Content,
            ContentType = request.ContentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        if (request.Tags is { Count: > 0 })
            put.TagSet = request.Tags.Select(t => new Tag { Key = t.Key, Value = t.Value }).ToList();

        await _s3.PutObjectAsync(put, ct);
        return new ObjectKey(request.ObjectPath);
    }

    public async Task<Uri> GetTemporaryUrlAsync(ObjectKey key, TimeSpan validFor, CancellationToken ct)
    {
        var url = await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key.Path,
            Expires = DateTime.UtcNow.Add(validFor),
            Verb = HttpVerb.GET
        });
        return new Uri(url);
    }

    // ... DownloadAsync, DeleteAsync, GetMetadataAsync similar
}
```

### Upload Endpoint

```csharp
// in Transport.Manual.Presentation — OperatorUploadEndpoints.cs
group.MapPost("/uploads/photo", async (
    HttpRequest request,
    IObjectStorageService storage,
    IImageProcessor imageProcessor,
    ICurrentOperator currentOp,
    CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Multipart form required");

    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("photo");
    var tripId = form["tripId"].ToString();
    var type = form["type"].ToString();   // "pickup" | "drop" | "exception"

    if (file is null || file.Length == 0)
        return Results.BadRequest("Photo required");

    if (file.Length > 5 * 1024 * 1024)
        return Results.BadRequest("Photo too large (max 5MB)");

    // Validate magic bytes
    if (!await imageProcessor.IsValidImageAsync(file.OpenReadStream(), ct))
        return Results.BadRequest("Invalid image");

    // Process: re-encode + strip EXIF
    var processed = await imageProcessor.NormalizeAsync(file.OpenReadStream(), ct);

    // Upload
    var objectPath = $"pod/{tripId}/{type}-photo/{Guid.NewGuid()}.jpg";
    var key = await storage.UploadAsync(new UploadRequest(
        Content: processed,
        ContentType: "image/jpeg",
        ContentLength: processed.Length,
        ObjectPath: objectPath,
        Tags: new Dictionary<string, string>
        {
            ["trip_id"] = tripId,
            ["operator_id"] = currentOp.OperatorId.ToString(),
            ["type"] = type
        }), ct);

    // Generate browser-accessible URL (temporary)
    var tempUrl = await storage.GetTemporaryUrlAsync(key, TimeSpan.FromMinutes(5), ct);

    return Results.Ok(new {
        url = tempUrl.ToString(),
        objectPath = key.Path,
        contentSha256 = imageProcessor.ComputeSha256(processed)
    });
});
```

### Image Processor

```csharp
public interface IImageProcessor
{
    Task<bool> IsValidImageAsync(Stream stream, CancellationToken ct);
    Task<Stream> NormalizeAsync(Stream stream, CancellationToken ct);
    string ComputeSha256(Stream stream);
}
```

Implementation ใช้ ImageSharp library:
- Re-encode to JPEG quality 85
- Resize if > 2048px max dimension
- Strip EXIF except GPS + timestamp
- Return processed stream

### Local MinIO Setup

Add to `docker-compose.yml`:

```yaml
services:
  minio:
    image: minio/minio:latest
    ports:
      - "9000:9000"
      - "9001:9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    volumes:
      - minio_data:/data
    command: server /data --console-address ":9001"

  minio-init:
    image: minio/mc:latest
    depends_on: [minio]
    entrypoint: >
      sh -c "
      mc alias set local http://minio:9000 minioadmin minioadmin;
      mc mb local/dtms-pod-dev || true;
      mc mb local/dtms-pod-test || true;
      "
```

## Edge Cases & Failure Modes

### Edge Case 1: Upload Succeeds, POD Capture Fails

Scenario: Photo uploaded to S3; subsequent `POST /trips/{id}/pickup` fails

**Behavior:**
- Object key not persisted in `manual_trip_extensions.pod_photo_url`
- Orphan object in S3
- Cleanup: nightly job scans `pod/*/{guid}.jpg` against extension table, deletes orphans > 24 hr old

```csharp
public sealed class OrphanPodCleanupService : BackgroundService
{
    // Daily scan: find S3 objects not referenced in extension tables → delete
}
```

### Edge Case 2: Mobile App Re-uploads Same Photo

Scenario: Network glitch causes retry — same content uploaded twice

**Handling:**
- Each upload gets new GUID → 2 objects in S3
- Idempotency via `contentSha256` returned to client; client can use same URL for both retries
- Better: client computes SHA-256 first, sends in header; server returns existing URL if found

```http
POST /api/operator/uploads/photo
Content-Sha256: abc123...

→ Server checks existing object with this SHA tag
→ If found: return existing URL (no re-upload)
→ If not: proceed with upload
```

### Edge Case 3: S3 Outage

Scenario: AWS S3 down during operator pickup

**Behavior:**
- Upload endpoint returns 503
- Mobile app queues upload (offline support per [Phase 4](../phases/phase-4-transport-manual.md))
- Operator continues with trip (server can accept pickup capture with empty `photoUrl` if `PhotoRequired=false`)
- Or: operator delayed until upload succeeds (if `PhotoRequired=true`)

**Mitigation:** Multi-region S3 backup + CloudFront fallback (post-launch hardening)

### Edge Case 4: Bucket Permissions Misconfigured

Scenario: New deployment, S3 bucket policy doesn't grant DTMS write access

**Handling:**
- Startup health check: `IObjectStorageService.HealthCheckAsync()` — uploads + deletes a small probe object
- Health check fails → app fails to start (fail fast)

### Edge Case 5: GDPR Delete Request for Recipient Signature

Scenario: Customer requests deletion of signature (personal data)

**Handling:**
- `POST /api/admin/pod/delete?contentSha256=...`
- Server finds objects with matching SHA tag → deletes
- Update `manual_trip_extensions.pod_signature_url` to NULL with audit reason
- Audit log: deletion event with requester + reason

## Cost Analysis

### Production projection (Year 1)

Assumptions:
- 1000 operators
- 5 trips/day each
- 2 photos + 1 signature per trip (avg 400KB total per trip)
- 365 days operation

```
Photos: 1000 × 5 × 365 = 1.825M trips/year
Storage: 1.825M × 400KB = 730 GB/year
Year 1 storage cost: 730GB × $0.023/GB/month × 12 = $201/year

After lifecycle (90d STANDARD → IA → 365d GLACIER):
- 90d STANDARD: ~180GB × $0.023 = $4.14/month
- 275d IA: ~550GB × $0.0125 = $6.88/month
- Total Y1: ~$130

Requests:
- Uploads: 1.825M PUTs × $0.005/1000 = $9/year
- Downloads (10% accessed): 182k GETs × $0.0004/1000 = $0.07/year

Total Y1 cost: ~$140 (very reasonable)
```

→ Cost trivial at projected scale; doesn't constrain decisions

### Local Dev Cost: $0 (MinIO container)

## Consequences

### Positive

- ✓ Industry-standard S3 protocol (portable, hireable skill)
- ✓ Local dev parity via MinIO (no AWS account needed)
- ✓ Lifecycle automation (Glacier transition) handles long retention cheaply
- ✓ Server-mediated upload enables validation + normalization
- ✓ Temporary URLs offload bandwidth from DTMS

### Negative

- ✗ Vendor coupling to S3 protocol (mitigated: portable to B2, Wasabi, Azure with adapter)
- ✗ Additional infrastructure to manage (bucket policies, lifecycle rules)
- ✗ Mobile app upload happens via DTMS (slower than direct-to-S3) — acceptable for ≤ 500KB
- ✗ Image processing adds CPU load on DTMS — needs benchmarking; offload to dedicated worker if needed

### Neutral

- Existing `ProofOfDelivery.PhotoUrl` schema works as-is (just stores returned URL)
- Cross-mode applicability: Fleet provider may have own POD storage — use FleetTripExtension fields differently per provider

## Acceptance Criteria

- [ ] `IObjectStorageService` interface defined in Transport.Manual.Application
- [ ] `S3ObjectStorageService` impl in Transport.Manual.Infrastructure
- [ ] Local MinIO added to docker-compose.yml + auto-initialized
- [ ] Upload endpoint `/api/operator/uploads/photo` with validation
- [ ] Image normalization service (re-encode + EXIF strip)
- [ ] Temporary signed URL generation working for dispatcher console access
- [ ] Lifecycle policy configured for production bucket (90d IA, 365d Glacier)
- [ ] Orphan cleanup background job
- [ ] Startup health check verifies S3 connectivity + bucket access
- [ ] Integration tests use Testcontainers MinIO

## Related ADRs

- [ADR-006](adr-006-transport-mode-feature-flag.md) — `Manual.PodStorage` config block
- [ADR-007](adr-007-mobile-api-authentication.md) — Upload endpoint auth + audit
- [ADR-008](adr-008-migration-strategy.md) — No DB schema impact (URLs are strings)

## References

- AWS S3 best practices: https://docs.aws.amazon.com/s3/latest/userguide/best-practices.html
- MinIO docs: https://min.io/docs/minio/linux/index.html
- ImageSharp: https://docs.sixlabors.com/articles/imagesharp/
- Existing entity: [ProofOfDelivery.cs](../../../src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Entities/ProofOfDelivery.cs)
- [Phase 4 — POD capture flow](../phases/phase-4-transport-manual.md#step-5-mobile-api-endpoints)
