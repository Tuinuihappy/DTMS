using DTMS.SharedKernel.Storage;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Infrastructure.Storage;

// Idempotent startup hook that creates the buckets if MinIO is freshly
// provisioned. Runs once on boot and exits; failures are logged but DO NOT
// crash the host — an upload failing loudly on first attempt is better than
// refusing to start the whole API because MinIO is briefly unavailable.
public sealed class ObjectStorageBucketInitializer : IHostedService
{
    private readonly IObjectStorageService _storage;
    private readonly ObjectStorageOptions _options;
    private readonly ILogger<ObjectStorageBucketInitializer> _logger;

    public ObjectStorageBucketInitializer(
        IObjectStorageService storage,
        IOptions<ObjectStorageOptions> options,
        ILogger<ObjectStorageBucketInitializer> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var bucket in new[] { _options.PodBucket, _options.AttachmentBucket })
        {
            if (string.IsNullOrWhiteSpace(bucket)) continue;
            try
            {
                await _storage.EnsureBucketExistsAsync(bucket, cancellationToken);
            }
            catch (Exception ex)
            {
                // Per-bucket so one unreachable bucket does not skip the others.
                _logger.LogError(ex,
                    "ObjectStorage: failed to ensure bucket '{Bucket}' on startup. " +
                    "Upload calls against it will fail until it is reachable.",
                    bucket);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
