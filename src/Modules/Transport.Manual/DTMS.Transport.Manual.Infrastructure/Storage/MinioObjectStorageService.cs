using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace AMR.DeliveryPlanning.Transport.Manual.Infrastructure.Storage;

// Phase 4.3 — MinIO-backed IObjectStorageService.
//
// Two-client design: one client targets the server-side endpoint (for
// HEAD checks, bucket creation — server-to-server traffic that never
// leaves the docker network), and a SECOND client targets the public
// endpoint just to compute presigned URLs (so the URL host is what the
// PWA can actually reach from the browser). Minio SDK bakes the host
// into the URL it returns from PresignedPutObjectAsync — there's no
// "override host on presign" knob — so the second client is the
// idiomatic workaround.
//
// Bucket policy is left at MinIO default (private). Presigned URLs are
// the only access path; uploads die after the expiry window with a 403.
public sealed class MinioObjectStorageService : IObjectStorageService
{
    // Presigned URL TTL is capped at 7 days by the AWS Signature V4 spec
    // MinIO implements — guard here so callers don't pass something
    // longer and silently get a 400 from MinIO at presign time.
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

        // Parse the public endpoint as URI so we can split scheme + host
        // for the second client. PublicEndpoint is the full URL the PWA
        // hits ("http://localhost:9000"); MinioClient.WithEndpoint takes
        // host[:port] + a separate UseSsl flag.
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

    public async Task<PresignedUploadUrl> GeneratePresignedPutAsync(
        string bucket,
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default)
    {
        if (expiresIn > MaxPresignTtl)
            throw new ArgumentOutOfRangeException(nameof(expiresIn),
                $"Presigned URL TTL must be ≤ 7 days (got {expiresIn}).");

        var args = new PresignedPutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiresIn.TotalSeconds);

        // Sign against the PUBLIC endpoint so the browser's PUT resolves.
        var url = await _publicClient.PresignedPutObjectAsync(args);

        // Stamp Content-Type into the returned signed URL? No — Minio
        // SDK doesn't include header pre-signing in the basic args; if a
        // caller needs to enforce content-type on upload they'd do it
        // client-side by setting the header on the PUT. Worth revisiting
        // in Phase 4.5 if the PWA wants stricter validation.
        _ = contentType;

        return new PresignedUploadUrl(
            UploadUrl: url,
            ObjectKey: objectKey,
            ExpiresAt: DateTime.UtcNow.Add(expiresIn));
    }

    public async Task<bool> ObjectExistsAsync(string bucket, string objectKey, CancellationToken ct = default)
    {
        try
        {
            await _internalClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
        catch (BucketNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ObjectStorage: stat {Bucket}/{Key} threw unexpected error.", bucket, objectKey);
            return false;
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
