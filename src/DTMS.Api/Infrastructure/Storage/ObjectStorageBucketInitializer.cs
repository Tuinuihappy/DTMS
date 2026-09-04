using DTMS.Fleet.Application.Services;
using DTMS.SharedKernel.Storage;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Infrastructure.Storage;

// Idempotent startup hook that creates the buckets if MinIO is freshly
// provisioned, and installs the staging-prefix expiry rule. Runs once on boot
// and exits; failures are logged but DO NOT crash the host — an upload failing
// loudly on first attempt is better than refusing to start the whole API
// because MinIO is briefly unavailable.
public sealed class ObjectStorageBucketInitializer : IHostedService
{
    // Stable across deploys: the rule is matched by id, so renaming it would
    // leave the old one behind and start expiring the prefix twice over.
    public const string IncomingExpiryRuleId = "dtms-incoming-expiry";

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

                // The expiry rule below is a property of a bucket that exists.
                continue;
            }

            // Only the attachment bucket stages uploads under a prefix before
            // promoting them. POD writes straight to its final key, so there is
            // no staging garbage there for a rule to collect — its unreferenced
            // objects are a cleanup job (scripts/minio-orphan-sweep.ps1), not an
            // expiry policy, because age alone does not make a POD photo junk.
            if (!string.Equals(bucket, _options.AttachmentBucket, StringComparison.Ordinal)) continue;

            try
            {
                await _storage.EnsureExpiryRuleAsync(
                    bucket,
                    IncomingExpiryRuleId,
                    AttachmentObjectKey.IncomingPrefix,
                    _options.IncomingRetentionDays,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Losing the rule costs disk, not correctness: unconfirmed
                // uploads accumulate but nothing reads them. Not worth failing
                // a boot that would otherwise serve traffic fine.
                _logger.LogError(ex,
                    "ObjectStorage: failed to set lifecycle rule '{RuleId}' on '{Bucket}'. " +
                    "Abandoned uploads under '{Prefix}' will accumulate until it is set.",
                    IncomingExpiryRuleId, bucket, AttachmentObjectKey.IncomingPrefix);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
