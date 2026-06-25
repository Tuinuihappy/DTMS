namespace AMR.DeliveryPlanning.Transport.Manual.Application.Services;

// Tiny indirection so Application doesn't need to know about
// IOptions<ObjectStorageOptions> — Infrastructure registers a single-
// value provider during DI wire-up. Trivial interface but keeps the
// dependency direction clean (Application → no Infra packages).
public interface IPodBucketProvider
{
    string PodBucket { get; }
}
