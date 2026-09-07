using DTMS.SharedKernel.Storage;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.DataModel.ILM;
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

        var fields = new Dictionary<string, string>(formData)
        {
            // The SDK signs an ["eq","$Content-Type",...] condition into the
            // policy but does not return Content-Type among the form fields.
            // S3 evaluates conditions against form FIELDS, not the file part's
            // header, so omitting it makes the condition impossible to satisfy
            // and MinIO refuses every upload with 403 "Policy Condition failed".
            //
            // Sending it from here is also what makes the type trustworthy:
            // the value is ours, and a client that edits it invalidates the
            // signature. The stored content type is therefore pinned by the
            // server, not asserted by the uploader.
            ["Content-Type"] = constraints.ContentType
        };

        return new PresignedUpload(
            Url: uri.ToString(),
            Fields: fields,
            ObjectKey: objectKey,
            ExpiresAt: expiresAt);
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

    public async Task<ObjectDeleteOutcome> DeleteAsync(
        string bucket, string objectKey, CancellationToken ct = default)
    {
        try
        {
            await _internalClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
        }
        catch (ObjectNotFoundException)
        {
            // Already gone is the desired end state. Callers are redelivered
            // outbox instructions; treating the second attempt as a failure
            // would park the message in the DLQ after the first success.
            _logger.LogDebug("ObjectStorage: {Bucket}/{Key} already absent on delete.", bucket, objectKey);
            return ObjectDeleteOutcome.AlreadyAbsent;
        }
        catch (BucketNotFoundException)
        {
            // NOT "already absent". A missing bucket means the client is
            // looking somewhere wrong, and every key it is asked to remove will
            // survive — the failure this method exists to stop being silent.
            _logger.LogError(
                "ObjectStorage: bucket {Bucket} not found while deleting {Key} (endpoint={Endpoint}). " +
                "Nothing was removed.", bucket, objectKey, _options.Endpoint);
            return ObjectDeleteOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ObjectStorage: delete of {Bucket}/{Key} failed (endpoint={Endpoint}).",
                bucket, objectKey, _options.Endpoint);
            return ObjectDeleteOutcome.Failed;
        }

        // The remove was accepted; confirm it took effect. This exists because
        // an accepted-but-ineffective delete has already happened in this
        // system, and nothing in the response distinguished it from a real one.
        return await ConfirmGoneAsync(bucket, objectKey, ct);
    }

    /// <summary>
    /// Re-reads the object after a delete that reported success.
    ///
    /// <para>Deliberately does not reuse <see cref="StatAsync"/>: that one
    /// answers "is it there?" and folds every unexpected error into "no", which
    /// is the safe reading for a lookup and exactly the wrong one here — it
    /// would turn "could not check" into "confirmed gone" and re-hide the bug
    /// this method was added to expose.</para>
    /// </summary>
    private async Task<ObjectDeleteOutcome> ConfirmGoneAsync(
        string bucket, string objectKey, CancellationToken ct)
    {
        try
        {
            await _internalClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
        }
        catch (ObjectNotFoundException)
        {
            return ObjectDeleteOutcome.Deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ObjectStorage: could not verify that {Bucket}/{Key} was deleted (endpoint={Endpoint}). " +
                "Treating it as still present.", bucket, objectKey, _options.Endpoint);
            return ObjectDeleteOutcome.Failed;
        }

        _logger.LogError(
            "ObjectStorage: delete of {Bucket}/{Key} was accepted but the object is still there " +
            "(endpoint={Endpoint}).", bucket, objectKey, _options.Endpoint);
        return ObjectDeleteOutcome.Failed;
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

    public async Task EnsureExpiryRuleAsync(
        string bucket, string ruleId, string prefix, int days, CancellationToken ct = default)
    {
        // Measured against MinIO RELEASE.2025-01-20: a bucket with no lifecycle
        // document returns null here rather than throwing. The catch below is
        // for S3-compatible servers that surface the 404 as an exception
        // instead — same meaning, different shape.
        //
        // What must NOT happen either way is falling through to the write on a
        // read that failed for some other reason: the write replaces the whole
        // document, so it would drop rules we never got to see. Hence the
        // deliberately narrow filter.
        LifecycleConfiguration? existing;
        try
        {
            existing = await _internalClient.GetBucketLifecycleAsync(
                new GetBucketLifecycleArgs().WithBucket(bucket), ct);
        }
        catch (Exception ex) when (IsMissingLifecycleConfiguration(ex))
        {
            existing = null;
        }

        var desired = PlanExpiryRule(existing, ruleId, prefix, days);
        if (desired is null)
        {
            _logger.LogDebug(
                "ObjectStorage: lifecycle rule '{RuleId}' on {Bucket} already current.", ruleId, bucket);
            return;
        }

        await _internalClient.SetBucketLifecycleAsync(
            new SetBucketLifecycleArgs().WithBucket(bucket).WithLifecycleConfiguration(desired), ct);

        _logger.LogInformation(
            "ObjectStorage: lifecycle rule '{RuleId}' on {Bucket} set to expire '{Prefix}' after {Days} day(s) ({Status}).",
            ruleId, bucket, prefix, Math.Max(1, days),
            days > 0 ? "enabled" : "disabled");
    }

    /// <summary>
    /// The decision half of <see cref="EnsureExpiryRuleAsync"/>, split out so it
    /// can be tested without a live MinIO — the interesting failure modes here
    /// are dropping a foreign rule and rewriting an unchanged one on every boot,
    /// neither of which a round trip against a real server would surface.
    /// </summary>
    /// <returns>
    /// The configuration to write, or <c>null</c> when the bucket already
    /// carries exactly this rule and nothing needs sending.
    /// </returns>
    public static LifecycleConfiguration? PlanExpiryRule(
        LifecycleConfiguration? existing, string ruleId, string prefix, int days)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            throw new ArgumentException("Rule id is required.", nameof(ruleId));

        // MinIO rejects Expiration.Days below 1 even on a disabled rule, so a
        // non-positive retention is expressed through Status, not through Days.
        var desired = new LifecycleRule
        {
            ID = ruleId,
            Status = days > 0
                ? LifecycleRule.LifecycleRuleStatusEnabled
                : LifecycleRule.LifecycleRuleStatusDisabled,
            Filter = new RuleFilter { Prefix = prefix },
            Expiration = new Expiration { Days = Math.Max(1, days) }
        };

        // Copy rather than mutate: on the "no change" path we return null and
        // the caller must be left holding exactly what the server reported.
        var rules = existing?.Rules is null
            ? new List<LifecycleRule>()
            : new List<LifecycleRule>(existing.Rules);

        var index = rules.FindIndex(r => string.Equals(r?.ID, ruleId, StringComparison.Ordinal));
        if (index >= 0)
        {
            if (Matches(rules[index], desired)) return null;
            rules[index] = desired;
        }
        else
        {
            rules.Add(desired);
        }

        return new LifecycleConfiguration(rules);
    }

    private static bool Matches(LifecycleRule? actual, LifecycleRule desired) =>
        actual is not null
        && string.Equals(actual.Status, desired.Status, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Filter?.Prefix, desired.Filter?.Prefix, StringComparison.Ordinal)
        && actual.Expiration?.Days == desired.Expiration?.Days;

    // S3's NoSuchLifecycleConfiguration reaches us as a generic
    // ErrorResponseException, so the code string in the message is the only
    // thing to match on. Every other failure has to propagate — see the call
    // site for why a failed read must never look like "no rules".
    private static bool IsMissingLifecycleConfiguration(Exception ex) =>
        ex.Message.Contains("NoSuchLifecycleConfiguration", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("lifecycle configuration does not exist", StringComparison.OrdinalIgnoreCase);
}
