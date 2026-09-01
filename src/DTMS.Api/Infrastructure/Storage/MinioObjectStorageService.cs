using DTMS.SharedKernel.Storage;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace DTMS.Api.Infrastructure.Storage;

// MinIO-backed IObjectStorageService. Lives at the composition root rather
// than in a module because more than one module needs it and ModuleBoundaryTests
// forbids a module reaching into another module's infrastructure to get it.
//
// Two-client design: one client targets the server-side endpoint (for
// HEAD checks, copies, deletes, bucket creation — server-to-server traffic
// that never leaves the docker network), and a SECOND client targets the
// public endpoint just to sign URLs and policies (so the host the browser
// is told to talk to is one it can actually reach). The SDK bakes the host
// into whatever it signs and offers no "override host" knob, so the second
// client is the idiomatic workaround.
//
// Bucket policy is left at MinIO default (private). Signed URLs are the only
// access path.
public sealed class MinioObjectStorageService : IObjectStorageService
{
    // AWS Signature V4 caps presigned lifetimes at 7 days; MinIO implements
    // the same limit. Guard here so a caller gets a clear exception instead
    // of an opaque 400 from MinIO at signing time.
    public static readonly TimeSpan MaxPresignTtl = TimeSpan.FromDays(7);

    private readonly IMinioClient _internalClient;
    private readonly IMinioClient _publicClient;
    private readonly ObjectStorageOptions _options;
    private readonly ILogger<MinioObjectStorageService> _logger;

    public MinioObjectStorageService(
        IOptions<ObjectStorageOptions> options,
        ILogger<MinioObjectStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _internalClient = new MinioClient()
            .WithEndpoint(_options.Endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.UseSsl)
            .Build();

        // PublicEndpoint is a full URL ("http://localhost:9000"); WithEndpoint
        // wants host[:port] plus a separate SSL flag.
        var publicUri = new Uri(_options.PublicEndpoint, UriKind.Absolute);
        var publicHostPort = publicUri.IsDefaultPort
            ? publicUri.Host
            : $"{publicUri.Host}:{publicUri.Port}";
        _publicClient = new MinioClient()
            .WithEndpoint(publicHostPort)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(string.Equals(publicUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            .Build();
    }

    public async Task<PresignedUpload> GeneratePresignedPostAsync(
        string bucket,
        string objectKey,
        UploadConstraints constraints,
        TimeSpan expiresIn,
        CancellationToken ct = default)
    {
        if (expiresIn > MaxPresignTtl)
            throw new ArgumentOutOfRangeException(nameof(expiresIn),
                $"Presigned TTL must be <= 7 days (got {expiresIn}).");

        var expiresAt = DateTime.UtcNow.Add(expiresIn);

        // Every condition below is signed into the policy and checked by MinIO
        // itself. This is the whole reason for preferring POST over a presigned
        // PUT: a PUT URL authorises "write anything of any size to this key",
        // so an oversized or wrong-typed body is only discoverable after the
        // bytes have already landed on disk.
        var policy = new PostPolicy();
        policy.SetBucket(bucket);
        policy.SetKey(objectKey);
        policy.SetExpires(expiresAt);
        policy.SetContentType(constraints.ContentType);
        policy.SetContentRange(constraints.MinBytes, constraints.MaxBytes);

        var args = new PresignedPostPolicyArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithPolicy(policy);

        // Signed against the PUBLIC endpoint so the browser's POST resolves.
        var (uri, formData) = await _publicClient.PresignedPostPolicyAsync(args);

        return new PresignedUpload(
            Url: uri.ToString(),
            Fields: new Dictionary<string, string>(formData),
            ObjectKey: objectKey,
            ExpiresAt: expiresAt);
    }

    // Transitional — removed together with the POD client migration. See the
    // interface for why it still exists.
    public async Task<PresignedPutUrl> GeneratePresignedPutAsync(
        string bucket,
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default)
    {
        if (expiresIn > MaxPresignTtl)
            throw new ArgumentOutOfRangeException(nameof(expiresIn),
                $"Presigned TTL must be <= 7 days (got {expiresIn}).");

        var url = await _publicClient.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithExpiry((int)expiresIn.TotalSeconds));

        // A presigned PUT signature cannot carry a content-type condition, so
        // this argument has never been enforceable. That is precisely the gap
        // GeneratePresignedPostAsync closes.
        _ = contentType;

        return new PresignedPutUrl(url, objectKey, DateTime.UtcNow.Add(expiresIn));
    }

    public async Task<string> GeneratePresignedGetAsync(
        string bucket,
        string objectKey,
        TimeSpan expiresIn,
        string? responseContentType = null,
        CancellationToken ct = default)
    {
        if (expiresIn > MaxPresignTtl)
            throw new ArgumentOutOfRangeException(nameof(expiresIn),
                $"Presigned TTL must be <= 7 days (got {expiresIn}).");

        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiresIn.TotalSeconds);

        // Force the content type the caller vouches for instead of the one the
        // object was stored with. MinIO echoes back whatever the uploader sent,
        // so an object stored as image/svg+xml would otherwise be served as
        // SVG — which executes script on the storage origin. The override is
        // part of the signature, so it cannot be stripped from the URL.
        if (!string.IsNullOrWhiteSpace(responseContentType))
        {
            args = args.WithHeaders(new Dictionary<string, string>
            {
                ["response-content-type"] = responseContentType
            });
        }

        return await _publicClient.PresignedGetObjectAsync(args);
    }

    public async Task CopyAsync(
        string bucket, string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        var source = new CopySourceObjectArgs()
            .WithBucket(bucket)
            .WithObject(sourceKey);

        await _internalClient.CopyObjectAsync(
            new CopyObjectArgs()
                .WithBucket(bucket)
                .WithObject(destinationKey)
                .WithCopyObjectSource(source),
            ct);
    }

    public async Task DeleteAsync(string bucket, string objectKey, CancellationToken ct = default)
    {
        try
        {
            await _internalClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
        }
        catch (ObjectNotFoundException)
        {
            // Already gone is the desired end state. Callers are redelivered
            // outbox instructions; throwing here would park the row in the DLQ
            // on every retry after the first successful delete.
            _logger.LogDebug("ObjectStorage: {Bucket}/{Key} already absent on delete.", bucket, objectKey);
        }
        catch (BucketNotFoundException)
        {
            _logger.LogWarning("ObjectStorage: bucket {Bucket} missing on delete of {Key}.", bucket, objectKey);
        }
    }

    public async Task<bool> ObjectExistsAsync(string bucket, string objectKey, CancellationToken ct = default)
        => (await StatAsync(bucket, objectKey, ct)).Exists;

    public async Task<ObjectMetadata> StatAsync(string bucket, string objectKey, CancellationToken ct = default)
    {
        try
        {
            var stat = await _internalClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);

            // Size is measured by MinIO and can be trusted. ContentType is the
            // value the uploader sent, echoed back — a claim, not a fact. The
            // upload policy is what actually constrains it.
            return new ObjectMetadata(true, stat.Size, stat.ContentType ?? string.Empty);
        }
        catch (ObjectNotFoundException)
        {
            return ObjectMetadata.Missing;
        }
        catch (BucketNotFoundException)
        {
            return ObjectMetadata.Missing;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ObjectStorage: stat {Bucket}/{Key} threw unexpected error.", bucket, objectKey);
            return ObjectMetadata.Missing;
        }
    }

    public async Task EnsureBucketExistsAsync(string bucket, CancellationToken ct = default)
    {
        var exists = await _internalClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket), ct);
        if (exists)
        {
            _logger.LogDebug("ObjectStorage: bucket {Bucket} already exists.", bucket);
            return;
        }

        await _internalClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);
        _logger.LogInformation("ObjectStorage: created bucket {Bucket} (Endpoint={Endpoint}).",
            bucket, _options.Endpoint);
    }
}
