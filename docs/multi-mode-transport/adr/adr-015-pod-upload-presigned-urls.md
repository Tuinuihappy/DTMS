# ADR-015: POD Upload Mechanism — Presigned URLs (MinIO direct upload)

- **Status**: Accepted
- **Date**: 2026-06-25
- **Deciders**: Solo dev (DTMS)
- **Supersedes**: [ADR-009 POD Object Storage](adr-009-pod-object-storage.md) — upload mechanism only (backend choice unchanged)
- **Related**: [ADR-009 POD Object Storage](adr-009-pod-object-storage.md), [ADR-012 PWA Mobile Stack](adr-012-operator-mobile-stack-pwa.md), [Phase 4: Transport.Manual](../phases/phase-4-transport-manual.md)

## Context

ADR-009 (2026-06-22) chose **MinIO** (S3-compatible) as POD object storage backend — this remains correct. ADR-009 also chose **server-mediated uploads** (mobile app → DTMS API → MinIO) as the upload mechanism, citing:
- Server-side validation (size, magic bytes, dimensions)
- Re-encoding to JPEG quality 85 (~300KB)
- Anti-spoofing of file content

**Context shift since 2026-06-22:** mobile stack = PWA (per ADR-012). PWA + Service Worker have different upload constraints:
- Service Worker can intercept fetches → enables Background Sync retry
- Large multipart POSTs through Next.js API routes hit body size limits (4MB default)
- PWA users on cellular have variable upload speed → server-mediated = slow round-trip
- Operator captures POD then moves → connection may drop mid-upload

**Server-mediated drawbacks:**
- DTMS API server handles every byte → CPU + bandwidth bottleneck at scale (100+ ops × 5 POD/day × 500KB = 250MB/day per API instance)
- API server becomes single point of failure for upload
- Next.js API route body size limit complications
- Latency: PWA → API → MinIO = double upload time

**Presigned URL advantages for PWA:**
- PWA uploads direct to MinIO (offload from DTMS API)
- Server-side validation moves to **post-upload verification** (DTMS API checks MinIO object metadata + magic bytes after notification)
- Service Worker Background Sync can resume direct upload on connection failure
- Scales horizontally without API CPU bottleneck

## Decision

Use **presigned PUT URLs** for POD uploads. Keep MinIO backend (per ADR-009). Server-side validation runs **post-upload** (after operator notifies DTMS API of completed upload).

### Upload flow

```
1. Operator captures POD (photo + signature) in PWA
2. PWA: POST /api/operator/trips/{id}/pod/presign
   Body: { kind: "pickup" | "drop", contentType: "image/jpeg" }
   Response: { uploadUrl: <presigned PUT>, objectKey, expiresAt }

3. PWA: PUT <uploadUrl> with binary content
   (direct to MinIO — bypasses DTMS API)
   PWA can use Background Sync to retry on failure

4. PWA: POST /api/operator/trips/{id}/pod
   Body: { kind, objectKey, lat, lng, captureTime, signature (optional inline SVG) }
   
5. DTMS API:
   - Fetch object metadata from MinIO (HEAD request)
   - Verify size < 5MB, contentType matches
   - Verify magic bytes (first N bytes) match contentType (anti-spoofing)
   - Verify ETag is stable (object exists)
   - If all checks pass → create ProofOfDelivery row
   - If checks fail → DELETE object from MinIO + return 400
```

### Presigned URL configuration
- **Expiration**: 5 minutes (operator has time to upload, but limits replay)
- **Method**: PUT only (cannot DELETE or LIST via presigned)
- **Content-Type**: locked to what operator declared in presign request
- **Max size**: enforced by MinIO bucket policy (`max-content-length: 5MB`)
- **Object key pattern**: `pod/{yyyy-MM}/{tripId}/{podId}.{ext}` (per ADR-009 pattern)

### Validation moves to post-upload (server-side)
```csharp
public class PodFinalizationService
{
    public async Task<Result<ProofOfDelivery>> FinalizeAsync(
        Guid tripId, FinalizePodRequest req, Guid operatorId)
    {
        // 1. Object exists in MinIO?
        var metadata = await _minio.HeadObjectAsync("dtms-pod", req.ObjectKey);
        if (metadata is null)
            return Result.Failure("Object not found — upload may have failed");
        
        // 2. Size + content type checks
        if (metadata.ContentLength > 5 * 1024 * 1024)
        {
            await _minio.DeleteObjectAsync("dtms-pod", req.ObjectKey);
            return Result.Failure("File too large");
        }
        if (!ALLOWED_CONTENT_TYPES.Contains(metadata.ContentType))
        {
            await _minio.DeleteObjectAsync("dtms-pod", req.ObjectKey);
            return Result.Failure("Invalid content type");
        }
        
        // 3. Magic bytes check (anti-spoofing — first 16 bytes)
        var firstBytes = await _minio.GetObjectRangeAsync(
            "dtms-pod", req.ObjectKey, 0, 16);
        if (!MagicByteChecker.IsImageType(firstBytes, metadata.ContentType))
        {
            await _minio.DeleteObjectAsync("dtms-pod", req.ObjectKey);
            return Result.Failure("Content does not match declared type");
        }
        
        // 4. Geofence check (per ADR-016)
        var inside = await _geofence.IsInside(req.Lat, req.Lng, tripId);
        if (!inside && req.OverrideRequestId is null)
        {
            await _minio.DeleteObjectAsync("dtms-pod", req.ObjectKey);
            return Result.Failure("Outside geofence — request override first");
        }
        
        // 5. Create ProofOfDelivery row + AddDomainEvent
        var pod = ProofOfDelivery.Create(
            tripId: tripId, kind: req.Kind,
            objectKey: req.ObjectKey,
            lat: req.Lat, lng: req.Lng,
            signature: req.Signature,
            capturedAt: req.CaptureTime,
            outsideGeofence: !inside,
            overrideRequestId: req.OverrideRequestId);
        await _podRepo.AddAsync(pod);
        return Result.Success(pod);
    }
}
```

### Signature handling (special case)
Signatures are small (5-20KB SVG). Two options:
- **Option A (chosen)**: SVG inlined in POD finalize request body (skip MinIO entirely)
- **Option B**: Treat like photo (presign + upload + finalize)

→ Chose Option A for signatures because: smaller, faster, no presign round-trip, SVG is text-safe in JSON.

## Reasoning — Why presigned over server-mediated

### Bottleneck analysis (100 ops × 5 POD/day × ~500KB)

| Mechanism | DTMS API throughput need |
|---|---|
| Server-mediated | ~250MB/day per API instance + CPU for re-encoding |
| Presigned | ~0 (only metadata fetches at finalize, < 1KB each) |

### Latency analysis (operator on cellular, 1Mbps upload)

| Mechanism | Upload time per POD |
|---|---|
| Server-mediated | 500KB / 1Mbps × 2 (PWA→API→MinIO) = ~8s |
| Presigned | 500KB / 1Mbps × 1 (PWA→MinIO direct) = ~4s |

### Failure handling

| Mechanism | Mid-upload connection drop |
|---|---|
| Server-mediated | Lost upload, PWA retries full file → wastes operator data |
| Presigned + Background Sync | Service Worker resumes upload from queue, operator continues other work |

### Security boundary

Both mechanisms enforce same security model:
- Auth required (JWT on presign request)
- Size/type/content validation
- Geofence enforcement
- Audit trail

Only difference: when validation happens (during upload vs post-upload). Post-upload validation **still deletes** invalid objects — no different security outcome.

## Implementation Sketch

### `IObjectStorageService` interface (extends ADR-009)
```csharp
public interface IObjectStorageService
{
    // Existing from ADR-009
    Task<ObjectKey> UploadAsync(UploadRequest request, CancellationToken ct);
    Task<Stream> DownloadAsync(ObjectKey key, CancellationToken ct);
    Task<Uri> GetTemporaryUrlAsync(ObjectKey key, TimeSpan validFor, CancellationToken ct);
    Task DeleteAsync(ObjectKey key, CancellationToken ct);
    Task<ObjectMetadata?> GetMetadataAsync(ObjectKey key, CancellationToken ct);
    
    // NEW for presigned uploads (ADR-015)
    Task<PresignedUploadUrl> GeneratePresignedPutAsync(
        ObjectKey key, string contentType, long maxBytes,
        TimeSpan expiresIn, CancellationToken ct);
    
    // NEW for magic bytes check
    Task<byte[]> GetObjectRangeAsync(
        string bucket, ObjectKey key, long offset, int length, CancellationToken ct);
}

public sealed record PresignedUploadUrl(Uri Url, ObjectKey Key, DateTime ExpiresAt);
```

### MinIO bucket policy (max upload size enforcement)
```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Sid": "MaxSize5MB",
    "Effect": "Deny",
    "Principal": "*",
    "Action": "s3:PutObject",
    "Resource": "arn:aws:s3:::dtms-pod/*",
    "Condition": {
      "NumericGreaterThan": { "s3:max-content-length": 5242880 }
    }
  }]
}
```

### PWA upload with Background Sync
```typescript
async function uploadPod(tripId: string, podKind: 'pickup' | 'drop', blob: Blob) {
    // 1. Get presigned URL
    const { uploadUrl, objectKey } = await fetch(
        `/api/operator/trips/${tripId}/pod/presign`,
        { method: 'POST', body: JSON.stringify({ kind: podKind, contentType: blob.type }) }
    ).then(r => r.json());
    
    // 2. Direct upload to MinIO
    try {
        await fetch(uploadUrl, {
            method: 'PUT',
            headers: { 'Content-Type': blob.type },
            body: blob,
        });
    } catch (err) {
        // Network failed — queue for Service Worker Background Sync
        await queueUploadForRetry(tripId, objectKey, blob);
        throw new Error('Upload queued for retry');
    }
    
    // 3. Finalize
    const gps = await getCurrentPosition();
    await fetch(`/api/operator/trips/${tripId}/pod`, {
        method: 'POST',
        body: JSON.stringify({
            kind: podKind,
            objectKey,
            lat: gps.latitude,
            lng: gps.longitude,
            captureTime: new Date().toISOString(),
        }),
    });
}
```

## Consequences

### Positive
- ✅ ~50% upload latency reduction (single hop vs double)
- ✅ DTMS API offload (zero file bytes through API)
- ✅ Service Worker Background Sync resumes failed uploads
- ✅ Horizontal API scaling no longer bottlenecked by file uploads

### Negative
- ❌ Validation deferred to post-upload (vs blocking at upload time)
- ❌ Invalid uploads briefly consume MinIO storage before deletion
- ❌ Slightly more code (2 endpoints: presign + finalize, vs single upload endpoint)
- ❌ PWA must coordinate 2 requests (presign + finalize)

### Mitigation for deferred validation
- 5-minute presigned URL expiry limits abuse window
- Bucket policy caps object size (no 100GB upload spam)
- Post-upload DELETE for invalid objects (storage cleanup)
- Future: scheduled job to delete unfinalized objects > 1hr old

## Migration path (if Service Worker / Background Sync hits limits)

If PWA gives way to React Native (per ADR-012 migration path), the presigned URL pattern carries over:
- React Native uses `react-native-blob-util` for direct uploads to S3-compatible endpoints
- Background uploads via `react-native-background-upload`
- Same `IObjectStorageService.GeneratePresignedPutAsync` from DTMS API
- Subscription handling per platform (ADR-013 pattern)

## Why this supersedes ADR-009 (upload mechanism only)

ADR-009's MinIO backend + S3-compatible abstraction + per-env bucket strategy + lifecycle policies remain valid. The **upload mechanism** (server-mediated) was chosen under the assumption that mobile = native (with native HTTP libraries handling large uploads reliably). PWA + Service Worker shifts the calculus:
- Direct upload from PWA is well-supported by browsers (XHR, fetch)
- Background Sync handles network drops better than retrying through a server proxy
- Browser body size limits don't apply to presigned PUT (request goes direct to MinIO)
- Server-mediated would bottleneck Next.js API routes on large files

The change is **upload mechanism only**. Backend choice (MinIO), abstraction (`IObjectStorageService`), file format (JPEG photos, SVG signatures), bucket policies, and lifecycle remain per ADR-009.
