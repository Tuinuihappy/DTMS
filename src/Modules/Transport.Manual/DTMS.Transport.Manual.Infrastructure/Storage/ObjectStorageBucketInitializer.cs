using DTMS.Transport.Manual.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Transport.Manual.Infrastructure.Storage;

// Phase 4.3 — Idempotent startup hook that creates the POD bucket if
// MinIO is freshly provisioned. Runs once on app boot and exits;
// failures are logged but DO NOT crash the host (POD upload would just
// fail loudly on first attempt — better than refusing to start the
// whole API just because MinIO is briefly unavailable).
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
        try
        {
            await _storage.EnsureBucketExistsAsync(_options.PodBucket, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ObjectStorage: failed to ensure POD bucket '{Bucket}' on startup. " +
                "Upload calls will fail until the bucket is reachable.",
                _options.PodBucket);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
